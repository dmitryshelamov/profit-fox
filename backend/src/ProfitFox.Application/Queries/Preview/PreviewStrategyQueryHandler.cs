using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.MarketData.Converters;
using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The orders of a strategy and its cycles on one pair (change add-strategy-constructor, design.md, decision 6; change
///     add-position-node, decision 9). The request is checked whole before any history is read: the timeframe, the graph
///     with the signals it points at, the fees, the pair. The candles of the timeframe and the funding come from the memory
///     the preview of a signal shares. A strategy by plan reads the minutes of its weeks with orders only; a strategy with
///     orders reads those of the last weeks of exploration. Nothing is read past the end of exploration, the start of the
///     current UTC day; nothing goes to the queue, and no saved strategy or signal changes.
/// </summary>
internal sealed class PreviewStrategyQueryHandler(
    IHistorySource source,
    IInstrumentCatalog catalog,
    PreviewHistoryCache cache,
    ISignalRepository signals,
    TimeProvider time)
    : IRequestHandler<PreviewStrategyQuery, StrategyPreviewOutcomeInternal>
{
    /// <exception cref="HistoryUnavailableException">The market-data service did not give the pairs or the history.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled; the cycles stop being counted.</exception>
    public async Task<StrategyPreviewOutcomeInternal> Handle(PreviewStrategyQuery query, CancellationToken cancellationToken)
    {
        List<PreviewErrorInternal> errors = [];
        if (!Timeframes.TryParse(query.Timeframe, out TimeSpan timeframe))
        {
            errors.Add(new PreviewErrorInternal("InvalidTimeframe", $"the timeframe is one of {Timeframes.Listed}") { Field = "timeframe" });
        }

        TradingStrategy? strategy = null;
        if (query.Strategy is { } graph)
        {
            StrategyGraphCheck check = await StrategyChecks.CheckAsync(signals, graph, cancellationToken);
            strategy = check.Strategy;
            errors.AddRange(check.Errors.Select(PreviewErrorInternal.Of));
        }
        else
        {
            errors.Add(new PreviewErrorInternal("MissingField", "the strategy graph is required") { Field = "strategy" });
        }

        TradeCosts costs = TradeCosts.Default;
        if (Fee(query.TakerFee, "takerFee", "ставка тейкера", errors) is { } taker)
        {
            costs = costs with { TakerFee = taker };
        }

        if (Fee(query.MakerFee, "makerFee", "ставка мейкера", errors) is { } maker)
        {
            costs = costs with { MakerFee = maker };
        }

        Instrument? pair = null;
        IReadOnlyList<Instrument> pairs = [];
        if (query.Instrument is not { Length: > 0 } symbol)
        {
            errors.Add(new PreviewErrorInternal("MissingField", "the pair is required") { Field = "instrument" });
        }
        else
        {
            pairs = await catalog.LinearUsdtPerpetuals(cancellationToken);
            pair = pairs.FirstOrDefault(candidate => candidate.Symbol == symbol);
            if (pair is null)
            {
                errors.Add(new PreviewErrorInternal("UnknownInstrument", $"{symbol} is not a listed linear USDT perpetual") { Field = "instrument" });
            }
        }

        if (strategy is not null && pair is not null)
        {
            errors.AddRange(StrategyGraph.CheckNamedPairs(strategy.Graph, [.. pairs.Select(listed => listed.Symbol)]).Select(PreviewErrorInternal.Of));
        }

        if (errors.Count > 0 || strategy is null || pair is null)
        {
            return new StrategyPreviewOutcomeInternal(Preview: null, errors);
        }

        DateTimeOffset end = PreviewPeriod.End(time.GetUtcNow());
        bool openInterest = GraphNeeds.OpenInterest(strategy.Graph);
        PreviewHistory history = await cache.Get(pair.Id, timeframe, end, () => PreviewHistory.ReadAsync(source, pair.Id, timeframe, end, time, openInterest), cancellationToken, openInterest);
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? named = await PreviewHistory.NamedAsync(
            StrategyGraph.NamedPairs(strategy.Graph),
            pairs,
            cache,
            source,
            timeframe,
            end,
            time,
            cancellationToken);
        using var hash = new HistoryHash();
        var reader = new MarketHistory(source, end, hash, time);
        (IReadOnlyList<OrderOutcome> outcomes, IReadOnlyList<CycleOutcome> cycles, DateTimeOffset? computedFrom) = strategy.ByPlan
            ? await ByPlan(strategy, timeframe, pair, history, named, reader, end, costs, cancellationToken)
            : await WithOrders(strategy, timeframe, pair, history, named, reader, end, costs, cancellationToken);

        var preview = new StrategyPreviewInternal(
            pair.ToInternal(),
            timeframe,
            [.. history.Candles.Items.Where(candle => candle.HasTrades)],
            history.Candles.Coverage,
            outcomes,
            cycles,
            end,
            computedFrom);
        return new StrategyPreviewOutcomeInternal(preview, Errors: []);
    }

    // A strategy by plan (change add-position-node, design.md, decision 5): its steps ahead, the minutes of the weeks with
    // orders only, each cycle the trade of its plan.
    private static async Task<(IReadOnlyList<OrderOutcome>, IReadOnlyList<CycleOutcome>, DateTimeOffset?)> ByPlan(
        TradingStrategy strategy,
        TimeSpan timeframe,
        Instrument pair,
        PreviewHistory history,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? named,
        MarketHistory reader,
        DateTimeOffset end,
        TradeCosts costs,
        CancellationToken cancellationToken)
    {
        var steps = PairSteps.Of(strategy, timeframe, pair.Symbol, history.Candles.Items, history.Funding.Items, named, history.Points);
        var orders = new PairOrders(steps, HistoryPeriod.DataStart, end, costs);
        foreach (long week in orders.Weeks)
        {
            (DateTimeOffset from, DateTimeOffset to) = orders.Span(week);
            HistoryRead<HistoryCandle> minutes = await reader.Candles(pair.Id, TimeSpan.FromMinutes(1), from, to, cancellationToken);
            orders.Evaluate(week, MinutePaths.Build(from, to, minutes, history.Funding));
        }

        return (orders.Outcomes(), orders.Cycles(), orders.ComputedFrom);
    }

    // A strategy with orders (decision 6): the graph and the minutes of the last weeks of exploration together.
    private static async Task<(IReadOnlyList<OrderOutcome>, IReadOnlyList<CycleOutcome>, DateTimeOffset?)> WithOrders(
        TradingStrategy strategy,
        TimeSpan timeframe,
        Instrument pair,
        PreviewHistory history,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryCandle>>? named,
        MarketHistory reader,
        DateTimeOffset end,
        TradeCosts costs,
        CancellationToken cancellationToken)
    {
        (DateTimeOffset from, DateTimeOffset to) = PairCycles.Window(HistoryPeriod.DataStart, end);
        HistoryRead<HistoryCandle> minutes = await reader.Candles(pair.Id, TimeSpan.FromMinutes(1), from, to, cancellationToken);
        PositionResult result = PairCycles.Of(
            strategy,
            timeframe,
            history.Candles.Items,
            history.Funding.Items,
            MinutePaths.Build(from, to, minutes, history.Funding),
            from,
            to,
            costs,
            cancellationToken,
            named,
            history.Points);
        return (result.Orders, result.Cycles, from);
    }

    // A fee of the request: a fraction from 0 to below 1, or the default when not given.
    private static double? Fee(double? fee, string field, string name, List<PreviewErrorInternal> errors)
    {
        if (fee is not { } value)
        {
            return null;
        }

        if (value is >= 0 and < 1)
        {
            return value;
        }

        errors.Add(new PreviewErrorInternal("InvalidFee", $"{name} — доля суммы исполнения, не меньше 0 и меньше 1") { Field = field });
        return null;
    }
}
