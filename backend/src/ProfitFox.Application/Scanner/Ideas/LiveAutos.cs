using ProfitFox.Application.Backtests;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     What the autos do in a minute of the scanner (decisions 1, 2, 4, 9, 10 and 11). The autos that are on are read every minute. An idea born in
///     the minute is given to each of them in the order of the list: one that fits the conditions becomes, by the mode, a paper trade or a refusal
///     (checked here, with the rules of the pair the scanner keeps) or a request for a trading Worker, which does the checks that need the money of
///     the account. The open paper trades go on by the closed minutes of their pairs from where they stopped, up to the end of the coverage of the
///     pair, and by the stored minutes when the memory no longer holds them. The open trades live in memory and storage stays the truth:
///     <see cref="Commit" /> keeps what storage took, and <see cref="StartOver" /> reads them again.
/// </summary>
public sealed class LiveAutos(
    IIdeaAutoRepository autos,
    IPaperIdeaTradeRepository paper,
    IIdeaTradeRepository trades,
    IIdeaTradingSettingsRepository settings,
    IIdeaBucketRepository buckets,
    ICandleRepository candles,
    IIdeaAutoEntryRepository entries,
    IIdeaAutoRefusalRepository refusals,
    IdeasEngineVersion version,
    ScannerMarket market,
    BacktestSettings backtests,
    WorkerIdentity identity,
    TimeProvider time)
{
    private static readonly TimeSpan RefusalsKept = TimeSpan.FromDays(7);
    private static readonly TimeSpan EntryWait = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyDictionary<MarketCategory, FeeRates> _fees = backtests.Policy().DefaultFees;
    private Dictionary<Guid, PaperIdeaTrade>? _open;

    /// <param name="end">The end of the minute.</param>
    /// <param name="born">The ideas made in the minute, in the order they were made.</param>
    /// <param name="births">The minute of each pair that the ideas were born in.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>What to write with the ideas of the minute; none when the autos did nothing.</returns>
    public async Task<AutoMinute?> RunAsync(DateTimeOffset end, IReadOnlyList<Idea> born, IReadOnlyDictionary<Instrument, IdeaCandle> births, CancellationToken cancellationToken)
    {
        _open ??= (await paper.GetOpenAsync(cancellationToken)).ToDictionary(trade => trade.Trade.Id);
        IReadOnlyList<IdeaAuto> enabled = await autos.GetEnabledAsync(cancellationToken);
        var working = new Dictionary<Guid, PaperIdeaTrade>(_open);
        List<PaperIdeaTrade> stepped = await StepAsync(working, cancellationToken);
        var opened = new List<PaperIdeaTrade>();
        var entries = new List<IdeaAutoEntry>();
        var refusals = new List<IdeaAutoRefusal>();

        if (born.Count > 0 && enabled.Count > 0)
        {
            await TakeAsync(end, enabled, born, births, working, opened, entries, refusals, cancellationToken);
        }

        return stepped.Count + opened.Count + entries.Count + refusals.Count == 0 ? null : new AutoMinute(identity.Id, opened, stepped, entries, refusals);
    }

    /// <summary>Keeps in memory what storage took of the minute: the paper trades stepped, and those opened for the ideas it added.</summary>
    public void Commit(AutoMinute? minute, IReadOnlyCollection<Idea> added)
    {
        if (minute is null)
        {
            return;
        }

        var taken = added.Select(idea => idea.Id).ToHashSet();

        foreach (PaperIdeaTrade trade in minute.Stepped.Concat(minute.Opened.Where(trade => taken.Contains(trade.Trade.IdeaId))))
        {
            if (trade.Trade.IsOpen)
            {
                _open![trade.Trade.Id] = trade;
            }
            else
            {
                _open!.Remove(trade.Trade.Id);
            }
        }
    }

    /// <summary>Deletes the refusals made more than 7 days ago (decision 12).</summary>
    public Task RemoveOldRefusalsAsync(CancellationToken cancellationToken)
    {
        return refusals.DeleteBeforeAsync(time.GetUtcNow() - RefusalsKept, cancellationToken);
    }

    /// <summary>Turns the requests no Worker took within a minute of their writing into refusals «Не успел войти» (decision 6).</summary>
    public Task ExpireEntriesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        return entries.ExpireAsync(now - EntryWait, now, cancellationToken);
    }

    /// <summary>Drops the open paper trades of memory: they are read from storage again.</summary>
    public void StartOver()
    {
        _open = null;
    }

    private async Task TakeAsync(
        DateTimeOffset end,
        IReadOnlyList<IdeaAuto> enabled,
        IReadOnlyList<Idea> born,
        IReadOnlyDictionary<Instrument, IdeaCandle> births,
        Dictionary<Guid, PaperIdeaTrade> working,
        List<PaperIdeaTrade> opened,
        List<IdeaAutoEntry> entries,
        List<IdeaAutoRefusal> refusals,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IdeaBucket> bucketList = await buckets.GetAsync(version.Number, cancellationToken);
        IReadOnlyList<IdeaView> views = IdeaView.Of(born, bucketList, _fees);
        IdeaTradingSettings tradingSettings = await settings.GetAsync(cancellationToken);
        IReadOnlyCollection<IdeaTrade>? openReal = null;
        IReadOnlyDictionary<Guid, IdeaAuto>? every = null;
        DateTimeOffset now = time.GetUtcNow();

        foreach (IdeaAuto auto in enabled.OrderBy(auto => auto.Position))
        {
            for (int fired = 0; fired < born.Count; fired++)
            {
                Idea idea = born[fired];

                if (!AutoConditions.Match(idea, views[fired].Bucket, auto.Conditions))
                {
                    continue;
                }

                if (auto.Mode == IdeaAutoMode.Live)
                {
                    entries.Add(new IdeaAutoEntry(Guid.NewGuid(), idea.Id, auto.Id, auto.AccountId, auto.Amount, auto.Conditions.MinRiskReward, auto.Position, fired, end, now));

                    continue;
                }

                openReal ??= await trades.GetOpenAsync(cancellationToken);
                every ??= await autos.GetEveryAsync(cancellationToken);
                IdeaCandle birth = births[idea.Instrument];
                ScannerPair pair = market.Find(idea.Instrument)!;
                FeeRates fees = _fees[idea.Instrument.Category];
                var books = new AutoBooks(openReal, [.. working.Values.Where(trade => trade.Trade.IsOpen)], every);
                PaperEntryCheck check = PaperEntry.Check(auto, idea, birth, RulesOf(pair), fees, tradingSettings.MaxAmount, books);

                if (check.Refusals.Count > 0)
                {
                    refusals.Add(new IdeaAutoRefusal(
                        Guid.NewGuid(),
                        idea.Id,
                        auto.Id,
                        IdeaAutoMode.Paper,
                        now,
                        idea.Instrument,
                        idea.Setup,
                        idea.Interval,
                        idea.Track.Plan.Type,
                        idea.Track.Plan.Side,
                        [.. check.Refusals.Select(refusal => refusal.Reason)],
                        check.Refusals[0].Detail));

                    continue;
                }

                PaperIdeaTrade trade = PaperFill.Open(Guid.NewGuid(), idea, auto.Id, auto.AccountId, check.Size!, PromiseOf(idea, birth, bucketList), birth, fees);
                working[trade.Trade.Id] = trade;
                opened.Add(trade);
            }
        }
    }

    // What the idea promises at the price the paper trade enters at: the statistics of its bucket as the list of ideas shows them, and the money from that price.
    private IdeaPromise PromiseOf(Idea idea, IdeaCandle birth, IReadOnlyList<IdeaBucket> bucketList)
    {
        Idea atPrice = idea with { Track = idea.Track with { Plan = idea.Track.Plan with { Entry = PaperFill.EntryPrice(idea, birth) } } };
        IdeaView view = IdeaView.Of([atPrice], bucketList, _fees)[0];

        return new IdeaPromise(view.Bucket?.Badge, view.Probability, view.Bucket?.Expectancy, view.Bucket?.Cases, view.Shares.BreakEven, view.Shares.NetRiskReward);
    }

    // None while the list of instruments has not told the step and the minimums of the volume of the pair.
    private static InstrumentRules? RulesOf(ScannerPair pair)
    {
        return pair is { QuantityStep: { } step, MinQuantity: { } quantity, MinAmount: { } amount } ? new InstrumentRules(pair.TickSize ?? 0m, step, quantity, amount) : null;
    }

    private async Task<List<PaperIdeaTrade>> StepAsync(Dictionary<Guid, PaperIdeaTrade> working, CancellationToken cancellationToken)
    {
        var changed = new List<PaperIdeaTrade>();

        foreach (PaperIdeaTrade open in working.Values.Where(trade => trade.Trade.IsOpen).ToList())
        {
            if (market.Find(open.Trade.Instrument) is not { CoveredUntil: { } until })
            {
                continue;
            }

            FeeRates fees = _fees[open.Trade.Instrument.Category];
            PaperIdeaTrade current = open;

            foreach (IdeaCandle minute in await MinutesAsync(open.Trade.Instrument, open.SteppedUntil, until, cancellationToken))
            {
                current = PaperFill.Step(current, minute, fees);
            }

            if (current != open)
            {
                working[open.Trade.Id] = current;
                changed.Add(current);
            }
        }

        return changed;
    }

    // The closed minutes of the pair in [from, until): the stored ones before the memory, which holds the last day, then the ones in memory.
    private async Task<IReadOnlyList<IdeaCandle>> MinutesAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketMinute> memory = market.HoldsMinutes(instrument) ? market.Minutes(instrument) : [];
        DateTimeOffset memoryFrom = memory.Count > 0 ? memory[0].OpenTime : until;
        var minutes = new List<IdeaCandle>();

        if (from < memoryFrom)
        {
            DateTimeOffset to = memoryFrom < until ? memoryFrom : until;
            IReadOnlyList<Candle> stored = await candles.GetAsync(instrument, Interval.OneMinute, from, to, (int)(to - from).TotalMinutes, cancellationToken);
            minutes.AddRange(stored.Select(candle => new IdeaCandle(candle.OpenTime, candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Turnover.Value)));
        }

        minutes.AddRange(memory.Where(minute => minute.OpenTime >= from && minute.OpenTime < until)
            .Select(minute => new IdeaCandle(minute.OpenTime, minute.Open, minute.High, minute.Low, minute.Close, minute.Turnover)));

        return minutes;
    }
}