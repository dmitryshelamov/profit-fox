using System.Globalization;
using ProfitFox.Bench.MarketData;
using ProfitFox.Bench.MarketData.History;
using ProfitFox.Bench.Presentation.Errors;
using ProfitFox.Bench.Runs;
using ProfitFox.Bench.Signals;
using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Bench.Presentation.Preview;

/// <summary>
///     The preview of a signal on one pair over exploration (spec signal-preview), with the events computed like those of
///     a bench run. The request is checked whole before any history is read; history after the end of exploration is
///     never read; nothing goes to the ledger or the reports.
/// </summary>
public sealed class SignalPreview(IHistorySource source, InstrumentCatalog catalog)
{
    public async Task<PreviewResult> Run(PreviewRequest request, CancellationToken cancellation)
    {
        List<RequestError> errors = [];
        if (!Timeframes.TryParse(request.Timeframe, out TimeSpan timeframe))
        {
            errors.Add(new RequestError("InvalidTimeframe", "the timeframe is 1h, 4h or 1d") { Field = "timeframe" });
        }

        Signal? signal = null;
        if (request.Signal is { } graph)
        {
            ReadResult<Signal> read = StrategyGraph.ReadSignal(graph);
            signal = read.Value;
            errors.AddRange(read.Errors.Select(RequestErrors.Of));
        }
        else
        {
            errors.Add(new RequestError("MissingField", "the signal graph is required") { Field = "signal" });
        }

        Instrument? pair = null;
        if (request.Instrument is not { Length: > 0 } symbol)
        {
            errors.Add(new RequestError("MissingField", "the pair is required") { Field = "instrument" });
        }
        else
        {
            IReadOnlyList<Instrument> pairs = await catalog.LinearUsdtPerpetuals(cancellation);
            pair = pairs.FirstOrDefault(candidate => candidate.Symbol == symbol);
            if (pair is null)
            {
                errors.Add(new RequestError("UnknownInstrument", $"{symbol} is not a listed linear USDT perpetual") { Field = "instrument" });
            }
        }

        if (errors.Count > 0 || signal is null || pair is null)
        {
            return new PreviewResult(Preview: null, new ErrorResponse(errors));
        }

        using var hash = new HistoryHash();
        var history = new MarketHistory(source, Periods.ExplorationEnd, hash);
        HistoryRead<HistoryCandle> candles = await history.Candles(pair.Id, timeframe, Periods.DataStart, Periods.ExplorationEnd, cancellation);
        HistoryRead<HistoryPayment> funding = await history.FundingThrough(pair.Id, Periods.DataStart, Periods.ExplorationEnd, cancellation);
        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(signal, timeframe, pair.Symbol, candles.Items, funding.Items);

        var preview = new PreviewResponse(
            pair.Symbol,
            Timeframes.Name(timeframe),
            [.. candles.Items.Where(candle => candle.HasTrades).Select(Candle)],
            [.. candles.Coverage.Select(range => new PreviewRange(Ms(range.From), Ms(range.Until)))],
            [.. events.Select(moment => new PreviewEvent(Ms(moment)))],
            Ms(Periods.ExplorationEnd));
        return new PreviewResult(preview, Refused: null);
    }

    private static PreviewCandle Candle(HistoryCandle candle)
    {
        return new PreviewCandle(Ms(candle.OpenTime), Number(candle.Open), Number(candle.High), Number(candle.Low), Number(candle.Close), Number(candle.Volume));
    }

    private static double Number(string text)
    {
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static long Ms(DateTimeOffset time)
    {
        return time.ToUnixTimeMilliseconds();
    }
}
