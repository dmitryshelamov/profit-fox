using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Shared.Logging;
using ProfitFox.Tests.Unit.Application.Commands.Signals;
using ProfitFox.Tests.Unit.Application.Commands.Strategies;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     A scanner of ideas over fakes for the tests of change add-ideas-and-market (group 8): a clock from 10:00:05 of
///     2026-11-22, the strategies and signals in memory, recent history whose 1h and 4h candles close at 99, and the pairs
///     <c>BTCUSDT</c>, <c>ETHUSDT</c> and <c>SOLUSDT</c>. The strategy <c>close-up</c> enters long the first time the close of
///     a candle is above 100, for <c>after</c>.
/// </summary>
internal sealed class ScannerRig
{
    public static readonly DateTimeOffset Ten = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    public static readonly Instrument[] Pairs = [new Instrument("btc", "BTCUSDT"), new Instrument("eth", "ETHUSDT"), new Instrument("sol", "SOLUSDT")];

    public ScannerRig(Instrument[]? pairs = null, ListLogger? log = null, ScannerSettings? settings = null)
    {
        Log = log ?? new ListLogger();
        Instruments = pairs ?? Pairs;
        Time = new FakeTimeProvider(Ten.AddSeconds(5));
        foreach (Instrument pair in Instruments)
        {
            History.Candles[pair.Id] = (interval, before) => Warmup(interval, before, "99");
        }

        Scanner = new IdeaScanner(
            new MarketBook(Time, TimeSpan.FromSeconds(15)),
            History,
            Ideas,
            Market,
            new IdeaLibrary(Strategies, Signals, new MemoryIdeaSourceRepository(Strategies, Signals, Enabled, Labels)),
            Time,
            settings ?? new ScannerSettings(),
            Burst,
            Log);
    }

    /// <summary>
    ///     The journal of the scanner.
    /// </summary>
    public ListLogger Log { get; }

    public FakeTimeProvider Time { get; }

    /// <summary>
    ///     The pairs of the set of this rig.
    /// </summary>
    public Instrument[] Instruments { get; }

    /// <summary>
    ///     <paramref name="count" /> pairs <c>P01USDT</c>, <c>P02USDT</c>… with the ids <c>p01</c>, <c>p02</c>…
    /// </summary>
    public static Instrument[] Many(int count)
    {
        return [.. Enumerable.Range(1, count).Select(index => new Instrument($"p{index:D2}", $"P{index:D2}USDT"))];
    }

    public MemoryStrategyRepository Strategies { get; } = new MemoryStrategyRepository();

    public MemorySignalRepository Signals { get; } = new MemorySignalRepository();

    public MemoryStrategyIdeasRepository Enabled { get; } = new MemoryStrategyIdeasRepository();

    public MemorySignalLabelRepository Labels { get; } = new MemorySignalLabelRepository();

    public MemoryIdeaRepository Ideas { get; } = new MemoryIdeaRepository();

    public MemoryMarketPairRepository Market { get; } = new MemoryMarketPairRepository();

    public FakeRecentHistory History { get; } = new FakeRecentHistory();

    public IdeasSettings Burst { get; } = new IdeasSettings();

    public IdeaScanner Scanner { get; }

    /// <summary>
    ///     The candles of a warm-up: every candle of the interval from two days back with the close <paramref name="close" />.
    /// </summary>
    public static IReadOnlyList<HistoryCandle> Warmup(TimeSpan interval, DateTimeOffset before, string close)
    {
        List<HistoryCandle> candles = [];
        for (DateTimeOffset open = Ten.AddDays(-2); open + interval <= before; open += interval)
        {
            candles.Add(new HistoryCandle(open, "99", "100", "98", close, "10", Turnover: null, BuyVolume: null));
        }

        return candles;
    }

    /// <summary>
    ///     Saves <c>close-up</c> as <paramref name="name" /> in <paramref name="version" /> and enables it on the timeframes;
    ///     with <paramref name="stop" />, a stop at that distance.
    /// </summary>
    public async Task<Guid> Enable(
        string name,
        string after,
        int version = 4,
        Guid? id = null,
        string side = "long",
        string operation = "gt",
        decimal level = 100,
        decimal? stop = null,
        params TimeSpan[] timeframes)
    {
        return await Save(name, CloseUp(after, side, operation, level, stop), version, id, timeframes);
    }

    /// <summary>
    ///     Saves <c>funding-short</c> as <paramref name="name" /> and enables it on the timeframes: it enters short for
    ///     <paramref name="after" /> the first time the rate of the last payment is above <paramref name="level" />.
    /// </summary>
    public Task<Guid> EnableFunding(string name, string after, decimal level = 0.0005m, Guid? id = null, params TimeSpan[] timeframes)
    {
        return Save(name, FundingShort(after, level), version: 1, id, timeframes);
    }

    private async Task<Guid> Save(string name, string graph, int version, Guid? id, TimeSpan[] timeframes)
    {
        using var document = JsonDocument.Parse(graph);
        var strategy = StrategyDocument.Restore(id ?? Guid.NewGuid(), name, null, StrategyGraph.ReadForm(document.RootElement).Value!, new Dictionary<string, ProfitFox.Domain.ValueObjects.Signals.NodePosition>(), version, kind: StrategyKind.Idea);
        await Strategies.DeleteAsync(name, CancellationToken.None);
        await Strategies.CreateAsync(strategy, CancellationToken.None);
        await Enabled.SetAsync(new StrategyIdeas(strategy.Id, true, timeframes.Length == 0 ? [TimeSpan.FromHours(1)] : timeframes), CancellationToken.None);
        return strategy.Id;
    }

    /// <summary>
    ///     Starts the scanner on the three pairs and warms them up.
    /// </summary>
    public async Task Start()
    {
        await Scanner.StartAsync(CancellationToken.None);
        await Scanner.ApplyAsync(new MarketPairs(Instruments), CancellationToken.None);
        await Warm();
    }

    /// <summary>
    ///     A pass, the warm-up reads it started, and a pass that takes them.
    /// </summary>
    public async Task Warm()
    {
        await Scanner.TickAsync(CancellationToken.None);
        await Scanner.WarmupsReadAsync();
        await Scanner.TickAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Closed minutes of a pair from <paramref name="from" />, <paramref name="count" /> of them, each opening at 100 and
    ///     closing at <paramref name="close" />.
    /// </summary>
    public async Task Minutes(string symbol, DateTimeOffset from, int count, decimal close, decimal? low = null, decimal? high = null)
    {
        for (int minute = 0; minute < count; minute++)
        {
            await Scanner.ApplyAsync(
                new LiveMinute(symbol, from.AddMinutes(minute), 100, high ?? Math.Max(100, close), low ?? Math.Min(100, close), close, 1, close),
                CancellationToken.None);
        }
    }

    /// <summary>
    ///     The clock moved to <paramref name="now" /> and a pass; <paramref name="quiet" /> — the feed told nothing for a while
    ///     before it, as a live feed between its minutes, not one that catches up (task 19.9).
    /// </summary>
    public async Task At(DateTimeOffset now, bool quiet = true)
    {
        Time.SetUtcNow(now);
        if (quiet)
        {
            Scanner.Quiet();
        }

        await Scanner.TickAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Saves the signal <paramref name="name" /> in <paramref name="version" /> with <paramref name="graph" /> and puts its
    ///     label <paramref name="text" /> on <paramref name="timeframe" />.
    /// </summary>
    public async Task<Guid> Label(string name, string text, TimeSpan timeframe, string graph, int version = 1, Guid? id = null)
    {
        using var document = JsonDocument.Parse(graph);
        var signal = ProfitFox.Domain.Entities.Signals.SignalDocument.Restore(
            id ?? Guid.NewGuid(),
            name,
            null,
            StrategyGraph.ReadForm(document.RootElement).Value!,
            new Dictionary<string, ProfitFox.Domain.ValueObjects.Signals.NodePosition>(),
            version);
        if (await Signals.ExistsAsync(name, CancellationToken.None))
        {
            await Signals.DeleteAsync(name, CancellationToken.None);
        }

        await Signals.CreateAsync(signal, CancellationToken.None);
        await Labels.SetAsync(new SignalLabel(signal.Id, true, text, timeframe), CancellationToken.None);
        return signal.Id;
    }

    /// <summary>
    ///     A signal on the close of BTCUSDT against <paramref name="level" />: a state (<paramref name="state" />) true while
    ///     the close is below, or the event of the first time it is.
    /// </summary>
    public static string BtcBelow(decimal level, bool state = true)
    {
        string output = state ? "boolean" : "event";
        string nodes = state ? "" : """, { "id": "first", "kind": "first-time" }""";
        string wires = state
            ? """{ "from": "low.result", "to": "out.down" }"""
            : """{ "from": "low.result", "to": "first.value" }, { "from": "first.event", "to": "out.down" }""";
        return $$"""
                 {
                   "formatVersion": 1,
                   "outputs": [{ "name": "down", "type": "{{output}}" }],
                   "nodes": [
                     { "id": "btc", "kind": "pair-candle", "settings": { "symbol": "BTCUSDT" } },
                     { "id": "low", "kind": "compare", "settings": { "operation": "lt" }, "inputs": { "b": {{level}} } },
                     { "id": "out", "kind": "outputs" }{{nodes}}
                   ],
                   "wires": [{ "from": "btc.close", "to": "low.a" }, {{wires}}]
                 }
                 """;
    }

    private static string CloseUp(string after, string side, string operation, decimal level, decimal? stop)
    {
        string stopNode = stop is { } distance ? $$""", { "id": "stop", "kind": "stop-loss", "inputs": { "distance": {{Text(distance)}} } }""" : "";
        string stopWires = stop is null ? "" : """, { "from": "position.position", "to": "stop.position" }, { "from": "stop.order", "to": "position.orders" }""";
        return $$"""
                 {
                   "formatVersion": 1,
                   "nodes": [
                     { "id": "c", "kind": "candle" },
                     { "id": "up", "kind": "compare", "settings": { "operation": "{{operation}}" }, "inputs": { "b": {{level}} } },
                     { "id": "first", "kind": "first-time" },
                     { "id": "enter", "kind": "enter", "settings": { "side": "{{side}}" }, "inputs": { "size": 100 } },
                     { "id": "position", "kind": "position" },
                     { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }{{stopNode}}
                   ],
                   "wires": [
                     { "from": "c.close", "to": "up.a" },
                     { "from": "up.result", "to": "first.value" },
                     { "from": "first.event", "to": "enter.when" },
                     { "from": "enter.order", "to": "position.orders" },
                     { "from": "position.position", "to": "close-after.position" },
                     { "from": "close-after.order", "to": "position.orders" }{{stopWires}}
                   ]
                 }
                 """;
    }

    private static string FundingShort(string after, decimal level)
    {
        return $$"""
                 {
                   "formatVersion": 1,
                   "nodes": [
                     { "id": "f", "kind": "funding-rate" },
                     { "id": "up", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": {{Text(level)}} } },
                     { "id": "first", "kind": "first-time" },
                     { "id": "enter", "kind": "enter", "settings": { "side": "short" }, "inputs": { "size": 100 } },
                     { "id": "position", "kind": "position" },
                     { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }
                   ],
                   "wires": [
                     { "from": "f.value", "to": "up.a" },
                     { "from": "up.result", "to": "first.value" },
                     { "from": "first.event", "to": "enter.when" },
                     { "from": "enter.order", "to": "position.orders" },
                     { "from": "position.position", "to": "close-after.position" },
                     { "from": "close-after.order", "to": "position.orders" }
                   ]
                 }
                 """;
    }

    public static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
