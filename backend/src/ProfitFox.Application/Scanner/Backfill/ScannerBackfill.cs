using ProfitFox.Application.MarketData;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner.Backfill;

/// <summary>
///     Loads what storage misses of the pairs (decisions 4 and 8): first the last day of minutes and points of open interest of every pair
///     and the gap since its coverage, then the rest of the window of <see cref="ScannerSettings.HistoryDays" /> pair by pair from new to
///     old, then the funding of a linear pair. It asks the exchange only for what lies past the bounds of coverage, and a minute the
///     exchange leaves out of a loaded range had no trades: it is never stored.
/// </summary>
public sealed class ScannerBackfill(
    IMarketDataFeed feed,
    ICandleRepository candles,
    IOpenInterestRepository openInterest,
    IFundingRateRepository funding,
    IScannerPairRepository pairs,
    IScannerStateRepository state,
    ScannerMarket market,
    ScannerSettings settings,
    TimeProvider time)
{
    private const int MinutesPerDay = 1440;

    // The most minutes the exchange gives in one request.
    private const int PageSize = 1000;

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    // Of the pairs caught up in this session: up to where their older history goes, the start of the day memory holds.
    private readonly Dictionary<Instrument, DateTimeOffset> _historyUpTo = [];
    private readonly Dictionary<Instrument, DateTimeOffset> _openInterestUpTo = [];

    /// <summary>
    ///     The next piece of work: the last day of every pair not caught up in this session, or else the history of the next pair without
    ///     one; <c>false</c> when nothing is left.
    /// </summary>
    public async Task<bool> StepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScannerPair> listed = Listed();
        ScannerPair[] behind = [.. listed.Where(pair => !_historyUpTo.ContainsKey(pair.Instrument))];

        if (behind.Length > 0)
        {
            foreach (ScannerPair pair in behind)
            {
                await CatchUpAsync(pair.Instrument, cancellationToken);
            }

            await ProgressAsync(cancellationToken);

            return true;
        }

        ScannerPair? next = listed.FirstOrDefault(pair => pair.HistoryFrom is null);

        if (next is null)
        {
            return false;
        }

        await LoadHistoryAsync(next.Instrument, cancellationToken);

        return true;
    }

    /// <summary>
    ///     Brings the pair up to the last closed minute: the gap since its coverage, or its last day when it has none; after a restart memory
    ///     takes the day before the coverage from storage.
    /// </summary>
    public async Task CatchUpAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        DateTimeOffset end = LastClosedEnd();
        ScannerPair pair = market.Find(instrument)!;
        bool restart = !market.HoldsMinutes(instrument);

        if (pair.CoveredUntil is { } covered)
        {
            DateTimeOffset stored = Max(covered - Day, end - Day);

            if (restart && stored < covered)
            {
                market.Loaded(instrument, stored, covered, await candles.GetAsync(instrument, Interval.OneMinute, stored, covered, MinutesPerDay, cancellationToken));
            }

            await LoadMinutesAsync(instrument, covered, end, cancellationToken);
            _historyUpTo.TryAdd(instrument, covered - Day);
        }
        else
        {
            await LoadMinutesAsync(instrument, end - Day, end, cancellationToken);
            _historyUpTo.TryAdd(instrument, end - Day);
        }

        if (instrument.Category == MarketCategory.Linear)
        {
            await CatchUpOpenInterestAsync(instrument, pair.OpenInterestCoveredUntil, restart, end, cancellationToken);
        }
    }

    /// <summary>Reads the funding settlements of every linear pair since its funding ends.</summary>
    public async Task TopUpFundingAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset end = LastClosedEnd();

        foreach (ScannerPair pair in Listed().Where(pair => pair.Instrument.Category == MarketCategory.Linear && pair.FundingCoveredUntil < end))
        {
            await LoadFundingAsync(pair.Instrument, pair.FundingCoveredUntil!.Value, end, cancellationToken);
            market.Update(pair.Instrument, current => current with { FundingCoveredUntil = end });
        }
    }

    private async Task CatchUpOpenInterestAsync(Instrument instrument, DateTimeOffset? covered, bool restart, DateTimeOffset end, CancellationToken cancellationToken)
    {
        // A day and a step back: the change over 24 hours compares with the point of the step a day before the current one.
        if (covered is { } until)
        {
            DateTimeOffset stored = Max(until - Day - Step, end - Day - Step);

            if (restart && stored < until)
            {
                market.OpenInterestLoaded(instrument, stored, until, await openInterest.GetAsync(instrument, stored, until, (MinutesPerDay / 5) + 1, cancellationToken));
            }

            await LoadOpenInterestAsync(instrument, until, end, cancellationToken);
            _openInterestUpTo.TryAdd(instrument, until - Day - Step);
        }
        else
        {
            await LoadOpenInterestAsync(instrument, end - Day - Step, end, cancellationToken);
            _openInterestUpTo.TryAdd(instrument, end - Day - Step);
        }
    }

    /// <summary>
    ///     The older history of a pair, pages from new to old down to the start of the window, or to the launch of a younger pair when the
    ///     exchange tells it (linear only). A page without candles is passed on: a thin pair may trade nothing for long.
    /// </summary>
    private async Task LoadHistoryAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        DateTimeOffset end = LastClosedEnd();
        DateTimeOffset windowStart = end - TimeSpan.FromDays(settings.HistoryDays);
        DateTimeOffset historyFrom = Max(windowStart, market.Find(instrument)!.ListedAt ?? windowStart);

        for (DateTimeOffset to = _historyUpTo[instrument]; to > historyFrom; to = Max(historyFrom, to - (Minute * PageSize)))
        {
            IReadOnlyList<Candle> page = await feed.GetMinuteCandlesAsync(instrument, Max(historyFrom, to - (Minute * PageSize)), to, cancellationToken);
            await candles.UpsertAsync(instrument, Interval.OneMinute, page, cancellationToken);
        }

        bool linear = instrument.Category == MarketCategory.Linear;

        if (linear)
        {
            DateTimeOffset pointsUpTo = _openInterestUpTo[instrument];

            if (historyFrom < pointsUpTo)
            {
                await openInterest.UpsertAsync(instrument, await feed.GetOpenInterestAsync(instrument.Symbol, historyFrom, pointsUpTo, cancellationToken), cancellationToken);
            }

            await LoadFundingAsync(instrument, historyFrom, end, cancellationToken);
        }

        market.Update(instrument, pair => pair with { HistoryFrom = historyFrom, FundingCoveredUntil = linear ? end : pair.FundingCoveredUntil });
        await pairs.SaveAsync([market.Find(instrument)!], cancellationToken);
        await ProgressAsync(cancellationToken);
    }

    /// <summary>Every minute of <c>[from, to)</c>, pages from new to old; the minutes the exchange has go to storage, then to memory.</summary>
    private async Task LoadMinutesAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            return;
        }

        var loaded = new List<Candle>();

        for (DateTimeOffset upper = to; upper > from; upper = Max(from, upper - (Minute * PageSize)))
        {
            loaded.AddRange(await feed.GetMinuteCandlesAsync(instrument, Max(from, upper - (Minute * PageSize)), upper, cancellationToken));
        }

        await candles.UpsertAsync(instrument, Interval.OneMinute, loaded, cancellationToken);

        market.Loaded(instrument, from, to, loaded);
    }

    private async Task LoadOpenInterestAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            return;
        }

        IReadOnlyList<OpenInterestPoint> points = await feed.GetOpenInterestAsync(instrument.Symbol, from, to, cancellationToken);
        await openInterest.UpsertAsync(instrument, points, cancellationToken);
        market.OpenInterestLoaded(instrument, from, to, points);
    }

    private async Task LoadFundingAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await funding.UpsertAsync(instrument, await feed.GetFundingHistoryAsync(instrument.Symbol, from, to, cancellationToken), cancellationToken);
    }

    private Task ProgressAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScannerPair> listed = Listed();

        return state.SetProgressAsync(listed.Count, listed.Count(pair => pair.HistoryFrom is not null), cancellationToken);
    }

    private IReadOnlyList<ScannerPair> Listed()
    {
        return [.. market.Listed().OrderBy(pair => pair.Instrument.Category).ThenBy(pair => pair.Instrument.Symbol, StringComparer.Ordinal)];
    }

    // Bybit gives a closed minute a few seconds after its end, as the history of a chart allows for.
    private DateTimeOffset LastClosedEnd()
    {
        DateTimeOffset now = time.GetUtcNow() - CandleReadThrough.CloseMargin;

        return now.AddTicks(-(now.UtcTicks % Minute.Ticks));
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b)
    {
        return a > b ? a : b;
    }
}