using MediatR;

using ProfitFox.Application.Contracts.MarketData.Converters;
using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The events are computed like those of a bench run. The request is checked whole before any history is read; history
///     after the end of exploration is never read; nothing goes to the ledger or the reports. The history of the last pairs
///     stays in memory, so a second preview of the same pair and timeframe does not read it again.
/// </summary>
internal sealed class PreviewSignalQueryHandler(IHistorySource source, IInstrumentCatalog catalog, PreviewHistoryCache cache)
    : IRequestHandler<PreviewSignalQuery, PreviewOutcomeInternal>
{
    /// <exception cref="HistoryUnavailableException">The market-data service did not give the pairs or the history.</exception>
    public async Task<PreviewOutcomeInternal> Handle(PreviewSignalQuery query, CancellationToken cancellationToken)
    {
        List<PreviewErrorInternal> errors = [];
        if (!Timeframes.TryParse(query.Timeframe, out TimeSpan timeframe))
        {
            errors.Add(new PreviewErrorInternal("InvalidTimeframe", "the timeframe is 1h, 4h or 1d") { Field = "timeframe" });
        }

        Signal? signal = null;
        if (query.Signal is { } graph)
        {
            ReadResult<Signal> read = StrategyGraph.ReadSignal(graph);
            signal = read.Value;
            errors.AddRange(read.Errors.Select(PreviewErrorInternal.Of));
        }
        else
        {
            errors.Add(new PreviewErrorInternal("MissingField", "the signal graph is required") { Field = "signal" });
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

        if (errors.Count > 0 || signal is null || pair is null)
        {
            return new PreviewOutcomeInternal(Preview: null, errors);
        }

        PreviewHistory history = await cache.Get(pair.Id, timeframe, () => Read(pair.Id, timeframe), cancellationToken);
        HistoryRead<HistoryCandle> candles = history.Candles;
        HistoryRead<HistoryPayment> funding = history.Funding;
        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(signal, timeframe, pair.Symbol, candles.Items, funding.Items);

        var preview = new SignalPreviewInternal(
            pair.ToInternal(),
            timeframe,
            [.. candles.Items.Where(candle => candle.HasTrades)],
            candles.Coverage,
            events,
            Periods.ExplorationEnd);
        return new PreviewOutcomeInternal(preview, Errors: []);
    }

    // The whole of exploration, as a run of exploration reads it. The hash is not needed, but the reader wants one.
    private async Task<PreviewHistory> Read(string instrument, TimeSpan timeframe)
    {
        using var hash = new HistoryHash();
        var history = new MarketHistory(source, Periods.ExplorationEnd, hash);
        HistoryRead<HistoryCandle> candles = await history.Candles(instrument, timeframe, Periods.DataStart, Periods.ExplorationEnd, CancellationToken.None);
        HistoryRead<HistoryPayment> funding = await history.FundingThrough(instrument, Periods.DataStart, Periods.ExplorationEnd, CancellationToken.None);
        return new PreviewHistory(candles, funding);
    }
}
