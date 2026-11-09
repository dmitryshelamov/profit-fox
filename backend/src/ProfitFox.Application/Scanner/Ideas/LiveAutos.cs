using ProfitFox.Application.Backtests;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
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
///     the account. The open paper trades take the outcomes of the ideas that closed in the minute and go on by the closed minutes of their pairs
///     from where they stopped, up to the end of the coverage of the pair, and by the stored minutes when the memory no longer holds them; the
///     trade of a pair out of trading closes at expiry once its idea did. The open trades live in memory and storage stays the truth:
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
    /// <param name="outcomes">How the ideas that closed in the minute ended, put on their paper trades before they are stepped.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>What to write with the ideas of the minute; none when the autos did nothing.</returns>
    public async Task<AutoMinute?> RunAsync(
        DateTimeOffset end,
        IReadOnlyList<Idea> born,
        IReadOnlyDictionary<Instrument, IdeaCandle> births,
        IReadOnlyCollection<ClosedIdea> outcomes,
        CancellationToken cancellationToken)
    {
        _open ??= (await paper.GetOpenAsync(cancellationToken)).ToDictionary(trade => trade.Trade.Id);
        IReadOnlyList<IdeaAuto> enabled = await autos.GetEnabledAsync(cancellationToken);
        var working = new Dictionary<Guid, PaperIdeaTrade>(_open);
        Learn(working, outcomes);
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

    // The outcome of an idea goes on its paper trades before they are stepped, so a trade closes at expiry in the minute its idea expired.
    private static void Learn(Dictionary<Guid, PaperIdeaTrade> working, IReadOnlyCollection<ClosedIdea> outcomes)
    {
        foreach (ClosedIdea closed in outcomes)
        {
            foreach (PaperIdeaTrade paper in working.Values.Where(paper => paper.Trade.IdeaId == closed.IdeaId).ToList())
            {
                working[paper.Trade.Id] = paper with { Trade = paper.Trade with { IdeaOutcome = closed.Outcome } };
            }
        }
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
        IReadOnlyList<RiskRewardCurve> curves = await buckets.GetCurvesAsync(version.Number, cancellationToken);
        IReadOnlyList<IdeaView> views = IdeaView.Of(born, bucketList, curves, _fees);
        IdeaTradingSettings tradingSettings = await settings.GetAsync(cancellationToken);
        IReadOnlyCollection<IdeaTrade>? openReal = null;
        IReadOnlyDictionary<Guid, IdeaAuto>? every = null;
        DateTimeOffset now = time.GetUtcNow();

        IReadOnlyList<int> order = AutoMinutePlan.Order([.. views.Select(view => (view.Bucket, view.Shares))]);

        foreach (IdeaAuto auto in enabled.OrderBy(auto => auto.Position))
        {
            AutoMinutePlan? plan = null;

            // Decision 3 of add-burst-and-auto-limits: the ideas go by the order of their promise, and a request carries its place in it.
            for (int fired = 0; fired < order.Count; fired++)
            {
                Idea idea = born[order[fired]];

                if (!AutoConditions.Match(idea, views[order[fired]].Bucket, auto.Conditions))
                {
                    continue;
                }

                openReal ??= await trades.GetOpenAsync(cancellationToken);
                every ??= await autos.GetEveryAsync(cancellationToken);
                var books = new AutoBooks(openReal, [.. working.Values.Where(trade => trade.Trade.IsOpen)], every);

                // An auto that cannot hold one more trade leaves the idea alone: no trade, no request, no refusal.
                if (AutoBudget.IsFull(auto, books, tradingSettings))
                {
                    continue;
                }

                plan ??= new AutoMinutePlan(auto, books);

                if (!plan.Allows(idea))
                {
                    continue;
                }

                if (auto.Mode == IdeaAutoMode.Live)
                {
                    entries.Add(new IdeaAutoEntry(Guid.NewGuid(), idea.Id, auto.Id, auto.AccountId, auto.Amount, auto.Conditions.MinRiskReward, auto.Position, fired, end, now));
                    plan.Took(idea);

                    continue;
                }

                IdeaCandle birth = births[idea.Instrument];
                ScannerPair pair = market.Find(idea.Instrument)!;
                FeeRates fees = _fees[idea.Instrument.Category];
                PaperEntryCheck check = PaperEntry.Check(auto, idea, birth, RulesOf(pair), fees, tradingSettings.MaxAmount, tradingSettings.Leverage, books);

                if (check.Refusals.Count > 0)
                {
                    refusals.Add(Refusal(auto, idea, now, [.. check.Refusals.Select(refusal => refusal.Reason)], check.Refusals[0].Detail));

                    continue;
                }

                PaperIdeaTrade trade = PaperFill.Open(Guid.NewGuid(), idea, auto.Id, auto.AccountId, check.Size!, PromiseOf(idea, birth, bucketList, curves), birth, fees);
                working[trade.Trade.Id] = trade;
                opened.Add(trade);
                plan.Took(idea);
            }

            // Decision 4: the ideas a limit skipped are one refusal of the auto, the side and the minute, on the first of them.
            refusals.AddRange(plan?.Skips.Select(skip => Refusal(auto, skip.First, now, [skip.Reason], RefusalDetail.Numbers(skip.Skipped, skip.Limit))) ?? []);
        }
    }

    private static IdeaAutoRefusal Refusal(IdeaAuto auto, Idea idea, DateTimeOffset now, IReadOnlyList<IdeaTradeRefusal> reasons, RefusalDetail? detail)
    {
        return new IdeaAutoRefusal(
            Guid.NewGuid(),
            idea.Id,
            auto.Id,
            auto.Mode,
            now,
            idea.Instrument,
            idea.Setup,
            idea.Interval,
            idea.Track.Plan.Type,
            idea.Track.Plan.Side,
            reasons,
            detail);
    }

    // What the idea promises at the price the paper trade enters at: the statistics of its bucket as the list of ideas shows them, and the money from that price.
    private IdeaPromise PromiseOf(Idea idea, IdeaCandle birth, IReadOnlyList<IdeaBucket> bucketList, IReadOnlyList<RiskRewardCurve> curves)
    {
        Idea atPrice = idea with { Track = idea.Track with { Plan = idea.Track.Plan with { Entry = PaperFill.EntryPrice(idea, birth) } } };
        IdeaView view = IdeaView.Of([atPrice], bucketList, curves, _fees)[0];

        return new IdeaPromise(view.Bucket?.Badge, view.Numbers.Probability, view.Bucket?.Expectancy, view.Bucket?.Cases, view.Shares.BreakEven, view.Shares.NetRiskReward);
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
            ScannerPair? pair = market.Find(open.Trade.Instrument);
            FeeRates fees = _fees[open.Trade.Instrument.Category];
            PaperIdeaTrade current = open;

            if (pair is { CoveredUntil: { } until })
            {
                foreach (IdeaCandle minute in await MinutesAsync(open.Trade.Instrument, open.SteppedUntil, until, cancellationToken))
                {
                    current = PaperFill.Step(current, minute, fees);
                }
            }

            // A pair out of trading has no minutes to come: its trade closes at expiry once the scanner closed its idea at the last close.
            if (pair is not { Listed: true })
            {
                current = PaperFill.Expire(current, fees);
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