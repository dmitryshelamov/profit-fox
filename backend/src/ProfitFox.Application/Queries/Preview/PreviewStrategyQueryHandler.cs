using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.MarketData.Converters;
using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The orders of a strategy and their trades on one pair (change add-strategy-constructor, design.md, decision 6). The
///     request is checked whole before any history is read: the timeframe, the graph with the signals it points at, the
///     pair. The candles of the timeframe and the funding come from the memory the preview of a signal shares; the minutes
///     are read week by week for the computed weeks only, never past the end of exploration. Nothing goes to the ledger, the
///     reports or the queue, and no saved strategy or signal changes.
/// </summary>
internal sealed class PreviewStrategyQueryHandler(IHistorySource source, IInstrumentCatalog catalog, PreviewHistoryCache cache, ISignalRepository signals)
    : IRequestHandler<PreviewStrategyQuery, StrategyPreviewOutcomeInternal>
{
    /// <exception cref="HistoryUnavailableException">The market-data service did not give the pairs or the history.</exception>
    public async Task<StrategyPreviewOutcomeInternal> Handle(PreviewStrategyQuery query, CancellationToken cancellationToken)
    {
        List<PreviewErrorInternal> errors = [];
        if (!Timeframes.TryParse(query.Timeframe, out TimeSpan timeframe))
        {
            errors.Add(new PreviewErrorInternal("InvalidTimeframe", "the timeframe is 1h, 4h or 1d") { Field = "timeframe" });
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

        Instrument? pair = null;
        if (query.Instrument is not { Length: > 0 } symbol)
        {
            errors.Add(new PreviewErrorInternal("MissingField", "the pair is required") { Field = "instrument" });
        }
        else
        {
            IReadOnlyList<Instrument> pairs = await catalog.LinearUsdtPerpetuals(cancellationToken);
            pair = pairs.FirstOrDefault(candidate => candidate.Symbol == symbol);
            if (pair is null)
            {
                errors.Add(new PreviewErrorInternal("UnknownInstrument", $"{symbol} is not a listed linear USDT perpetual") { Field = "instrument" });
            }
        }

        if (errors.Count > 0 || strategy is null || pair is null)
        {
            return new StrategyPreviewOutcomeInternal(Preview: null, errors);
        }

        PreviewHistory history = await cache.Get(pair.Id, timeframe, () => PreviewHistory.ReadAsync(source, pair.Id, timeframe), cancellationToken);
        var steps = PairSteps.Of(strategy, timeframe, pair.Symbol, history.Candles.Items, history.Funding.Items);
        var orders = new PairOrders(steps, Periods.DataStart, Periods.ExplorationEnd, TradeCosts.Default);
        using var hash = new HistoryHash();
        var reader = new MarketHistory(source, Periods.ExplorationEnd, hash);
        foreach (long week in orders.Weeks)
        {
            (DateTimeOffset from, DateTimeOffset to) = orders.Span(week);
            HistoryRead<HistoryCandle> minutes = await reader.Candles(pair.Id, TimeSpan.FromMinutes(1), from, to, cancellationToken);
            orders.Evaluate(week, MinutePaths.Build(from, to, minutes, history.Funding));
        }

        var preview = new StrategyPreviewInternal(
            pair.ToInternal(),
            timeframe,
            [.. history.Candles.Items.Where(candle => candle.HasTrades)],
            history.Candles.Coverage,
            orders.Outcomes(),
            Periods.ExplorationEnd,
            orders.ComputedFrom);
        return new StrategyPreviewOutcomeInternal(preview, Errors: []);
    }
}
