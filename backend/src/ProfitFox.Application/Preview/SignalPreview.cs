using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Hypotheses;
using ProfitFox.Domain.MarketData;
using ProfitFox.Domain.Runs;
using ProfitFox.Domain.Signals;
using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Application.Preview;

/// <summary>
///     The preview of a signal on one pair over exploration (spec signal-preview), with the events computed like those of
///     a bench run. The request is checked whole before any history is read; history after the end of exploration is
///     never read; nothing goes to the ledger or the reports. The history of the last pairs stays in memory, so a second
///     preview of the same pair and timeframe does not read it again.
/// </summary>
public sealed class SignalPreview(IHistorySource source, IInstrumentCatalog catalog, PreviewHistoryCache cache)
{
    /// <exception cref="HistoryUnavailableException">The market-data service did not give the pairs or the history.</exception>
    public async Task<PreviewOutcome> Run(PreviewQuery query, CancellationToken cancellation)
    {
        List<PreviewError> errors = [];
        if (!Timeframes.TryParse(query.Timeframe, out TimeSpan timeframe))
        {
            errors.Add(new PreviewError("InvalidTimeframe", "the timeframe is 1h, 4h or 1d") { Field = "timeframe" });
        }

        Signal? signal = null;
        if (query.Signal is { } graph)
        {
            ReadResult<Signal> read = StrategyGraph.ReadSignal(graph);
            signal = read.Value;
            errors.AddRange(read.Errors.Select(PreviewError.Of));
        }
        else
        {
            errors.Add(new PreviewError("MissingField", "the signal graph is required") { Field = "signal" });
        }

        Instrument? pair = null;
        if (query.Instrument is not { Length: > 0 } symbol)
        {
            errors.Add(new PreviewError("MissingField", "the pair is required") { Field = "instrument" });
        }
        else
        {
            IReadOnlyList<Instrument> pairs = await catalog.LinearUsdtPerpetuals(cancellation);
            pair = pairs.FirstOrDefault(candidate => candidate.Symbol == symbol);
            if (pair is null)
            {
                errors.Add(new PreviewError("UnknownInstrument", $"{symbol} is not a listed linear USDT perpetual") { Field = "instrument" });
            }
        }

        if (errors.Count > 0 || signal is null || pair is null)
        {
            return new PreviewOutcome(Preview: null, errors);
        }

        PreviewHistory history = await cache.Get(pair.Id, timeframe, () => Read(pair.Id, timeframe), cancellation);
        HistoryRead<HistoryCandle> candles = history.Candles;
        HistoryRead<HistoryPayment> funding = history.Funding;
        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(signal, timeframe, pair.Symbol, candles.Items, funding.Items);

        var preview = new SignalPreviewResult(
            pair,
            timeframe,
            [.. candles.Items.Where(candle => candle.HasTrades)],
            candles.Coverage,
            events,
            Periods.ExplorationEnd);
        return new PreviewOutcome(preview, Errors: []);
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
