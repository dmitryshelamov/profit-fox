using MediatR;

using ProfitFox.Application.Contracts.MarketData.Converters;
using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Preview;

/// <summary>
///     The events are computed like the orders of a backtest. The request is checked whole before any history is read;
///     history after the end of exploration, the start of the current UTC day, is never read; nothing is written. The history of the last pairs
///     stays in memory, so a second preview of the same pair and timeframe does not read it again.
/// </summary>
internal sealed class PreviewSignalQueryHandler(IHistorySource source, IInstrumentCatalog catalog, PreviewHistoryCache cache, TimeProvider time)
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

        DateTimeOffset end = PreviewPeriod.End(time.GetUtcNow());
        PreviewHistory history = await cache.Get(pair.Id, timeframe, end, () => PreviewHistory.ReadAsync(source, pair.Id, timeframe, end), cancellationToken);
        HistoryRead<HistoryCandle> candles = history.Candles;
        HistoryRead<HistoryPayment> funding = history.Funding;
        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(signal, timeframe, pair.Symbol, candles.Items, funding.Items);

        var preview = new SignalPreviewInternal(
            pair.ToInternal(),
            timeframe,
            [.. candles.Items.Where(candle => candle.HasTrades)],
            candles.Coverage,
            events,
            end);
        return new PreviewOutcomeInternal(preview, Errors: []);
    }
}
