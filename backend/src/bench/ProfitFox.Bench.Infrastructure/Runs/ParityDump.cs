using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ProfitFox.Bench.Application.Runs;
using ProfitFox.Bench.Domain.Ledger;
using ProfitFox.Bench.Domain.MarketData;
using ProfitFox.Bench.Domain.Runs;
using ProfitFox.Engine.Simulation.Trades;

namespace ProfitFox.Bench.Infrastructure.Runs;

/// <summary>
///     What a run read and computed, written as gzip CSV for the check against the Python bench (design of
///     add-hypothesis-bench, decision 7). Times are Unix milliseconds, prices and rates the text the service sent. Event
///     outcomes are written only when the run computed them.
/// </summary>
public sealed class ParityDump : IParityDump
{
    private static readonly string[] OutcomeColumns =
        ["status", "exit_reason", "ambiguous", "entry_ms", "exit_ms", "entry_price", "exit_price", "gross", "fees", "slippage", "funding", "net", "r"];

    private readonly string _folder;
    private readonly StreamWriter _candles;
    private readonly StreamWriter _funding;
    private readonly StreamWriter _coverage;
    private readonly StreamWriter _minutes;
    private readonly StreamWriter _spans;
    private readonly StreamWriter _events;
    private readonly StreamWriter _randoms;
    private StreamWriter? _outcomes;

    public ParityDump(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
        _candles = Open("signal_candles", "instrument,open_ms,open,high,low,close,volume");
        _funding = Open("funding", "instrument,time_ms,rate");
        _coverage = Open("coverage", "feed,instrument,span_from_ms,from_ms,until_ms");
        _minutes = Open("minutes", "instrument,span_from_ms,span_to_ms,open_ms,open,high,low,close");
        _spans = Open("spans", "instrument,span_from_ms,span_to_ms");
        _events = Open("events", "instrument,moment_ms,block,status");
        _randoms = Open("randoms", "instrument,event_ms,entry_ms_planned," + string.Join(',', OutcomeColumns));
    }

    public void Write(PairData data, bool withEventOutcomes)
    {
        string symbol = data.Instrument.Symbol;
        foreach (HistoryCandle candle in data.SignalCandles.Items)
        {
            Row(_candles, symbol, Ms(candle.OpenTime), candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
        }

        foreach (TimeRange range in data.SignalCandles.Coverage)
        {
            Row(_coverage, "signal", symbol, "", Ms(range.From), Ms(range.Until));
        }

        foreach (HistoryPayment payment in data.Funding.Items)
        {
            Row(_funding, symbol, Ms(payment.Time), payment.Rate);
        }

        foreach (TimeRange range in data.Funding.Coverage)
        {
            Row(_coverage, "funding", symbol, "", Ms(range.From), Ms(range.Until));
        }

        foreach ((DateTimeOffset from, DateTimeOffset to, HistoryRead<HistoryCandle> minutes) in data.Spans)
        {
            Row(_spans, symbol, Ms(from), Ms(to));
            foreach (TimeRange range in minutes.Coverage)
            {
                Row(_coverage, "minutes", symbol, Ms(from), Ms(range.From), Ms(range.Until));
            }

            foreach (HistoryCandle candle in minutes.Items)
            {
                Row(_minutes, symbol, Ms(from), Ms(to), Ms(candle.OpenTime), candle.Open, candle.High, candle.Low, candle.Close);
            }
        }

        foreach (EventRow row in data.Rows)
        {
            Row(_events, symbol, Ms(row.Moment), row.Block.ToString(CultureInfo.InvariantCulture), ((int)row.Status).ToString(CultureInfo.InvariantCulture));
            foreach (RandomTrade trade in row.Randoms)
            {
                Row(_randoms, [symbol, Ms(row.Moment), Ms(trade.Entry), .. Outcome(trade.Outcome)]);
            }

            if (withEventOutcomes && row.Outcome is not null)
            {
                _outcomes ??= Open("event_outcomes", "instrument,moment_ms," + string.Join(',', OutcomeColumns));
                Row(_outcomes, [symbol, Ms(row.Moment), .. Outcome(row.Outcome)]);
            }
        }
    }

    public void Finish(
        IReadOnlyList<(long Block, double Mean, int Events)> pseudo,
        IReadOnlyList<(long Block, double Mean, int Events)>? blocks,
        LedgerEntry entry)
    {
        using (StreamWriter writer = Open("pseudo_blocks", "block,mean,events"))
        {
            foreach ((long block, double mean, int events) in pseudo)
            {
                Row(writer, block.ToString(CultureInfo.InvariantCulture), Number(mean), events.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (blocks is not null)
        {
            using StreamWriter writer = Open("blocks", "block,mean,events");
            foreach ((long block, double mean, int events) in blocks)
            {
                Row(writer, block.ToString(CultureInfo.InvariantCulture), Number(mean), events.ToString(CultureInfo.InvariantCulture));
            }
        }

        File.WriteAllText(Path.Combine(_folder, "run.json"), JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        _candles.Dispose();
        _funding.Dispose();
        _coverage.Dispose();
        _minutes.Dispose();
        _spans.Dispose();
        _events.Dispose();
        _randoms.Dispose();
        _outcomes?.Dispose();
    }

    private static string[] Outcome(TradeOutcome outcome)
    {
        return
        [
            ((int)outcome.Status).ToString(CultureInfo.InvariantCulture),
            ((int)outcome.ExitReason).ToString(CultureInfo.InvariantCulture),
            outcome.Ambiguous ? "True" : "False",
            outcome.EntryTime is { } entry ? Ms(entry) : "-1",
            outcome.ExitTime is { } exit ? Ms(exit) : "-1",
            Number(outcome.EntryPrice),
            Number(outcome.ExitPrice),
            Number(outcome.Gross),
            Number(outcome.Fees),
            Number(outcome.Slippage),
            Number(outcome.Funding),
            Number(outcome.Net),
            Number(outcome.R)
        ];
    }

    private StreamWriter Open(string name, string header)
    {
        var file = new FileStream(Path.Combine(_folder, $"{name}.csv.gz"), FileMode.Create);
        var writer = new StreamWriter(new GZipStream(file, CompressionLevel.Fastest), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(header);
        writer.Write('\n');
        return writer;
    }

    private static void Row(StreamWriter writer, params string[] fields)
    {
        writer.Write(string.Join(',', fields));
        writer.Write('\n');
    }

    private static string Ms(DateTimeOffset time)
    {
        return time.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }

    private static string Number(double? value)
    {
        return value is { } number && !double.IsNaN(number) ? number.ToString("R", CultureInfo.InvariantCulture) : "NaN";
    }
}
