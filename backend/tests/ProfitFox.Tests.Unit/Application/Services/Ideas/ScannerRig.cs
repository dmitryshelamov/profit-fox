using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Application.Services.Market;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
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
    ///     The minutes and the payments the feed told, in order: what <see cref="Backtest" /> gives a backtest.
    /// </summary>
    public List<LiveMinute> Told { get; } = [];

    public List<(string Symbol, DateTimeOffset Time, decimal Rate)> Paid { get; } = [];

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
    ///     with <paramref name="stop" />, a stop at that distance, and with <paramref name="target" />, a target.
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
        decimal? target = null,
        params TimeSpan[] timeframes)
    {
        return await Save(name, CloseUp(after, side, operation, level, stop, target), version, id, timeframes);
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
    ///     Closed minutes of a pair from <paramref name="from" />, <paramref name="count" /> of them, each opening at
    ///     <paramref name="open" /> and closing at <paramref name="close" />.
    /// </summary>
    public async Task Minutes(string symbol, DateTimeOffset from, int count, decimal close, decimal? low = null, decimal? high = null, decimal open = 100)
    {
        for (int minute = 0; minute < count; minute++)
        {
            var told = new LiveMinute(symbol, from.AddMinutes(minute), open, high ?? Math.Max(open, close), low ?? Math.Min(open, close), close, 1, close);
            Told.Add(told);
            await Scanner.ApplyAsync(told, CancellationToken.None);
        }
    }

    /// <summary>
    ///     The feed of payments tells the payment of the pair at <paramref name="time" />.
    /// </summary>
    public Task Pay(string symbol, DateTimeOffset time, decimal rate)
    {
        Paid.Add((symbol, time, rate));
        return Scanner.ApplyAsync(new LivePoint(LivePointKind.FundingPayment, symbol, time, rate), CancellationToken.None);
    }

    /// <summary>
    ///     What a backtest of the strategy <paramref name="name" /> gives on the pair over [10:00, <paramref name="end" />]
    ///     (task 17.4): the candles of the warm-up up to 10:00 and then those of the minutes told, the payments of the history
    ///     and of the feed, the minutes told as the path, the costs of an idea. Each order that opened a cycle, with it.
    /// </summary>
    public IReadOnlyList<(DateTimeOffset Moment, CycleOutcome Cycle)> Backtest(string name, TimeSpan timeframe, string symbol, DateTimeOffset end)
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(Strategies.Documents.Single(document => document.Name == name).Graph, StrategyKind.Idea).Value!;
        string id = Instruments.Single(pair => pair.Symbol == symbol).Id;
        HistoryCandle[] minutes =
        [
            .. Told.Where(minute => minute.Symbol == symbol).GroupBy(minute => minute.OpenTime).Select(same => same.Last()).OrderBy(minute => minute.OpenTime)
                .Select(minute => new HistoryCandle(minute.OpenTime, Text(minute.Open), Text(minute.High), Text(minute.Low), Text(minute.Close), Text(minute.Volume), Turnover: null, BuyVolume: null))
        ];
        List<HistoryCandle> candles = [.. History.Candles[id](timeframe, Ten)];
        for (DateTimeOffset open = Ten; open + timeframe <= end; open += timeframe)
        {
            HistoryCandle[] inside = [.. minutes.Where(minute => minute.OpenTime >= open && minute.OpenTime < open + timeframe)];
            if (inside.Length > 0)
            {
                decimal high = inside.Max(minute => decimal.Parse(minute.High, CultureInfo.InvariantCulture));
                decimal low = inside.Min(minute => decimal.Parse(minute.Low, CultureInfo.InvariantCulture));
                candles.Add(new HistoryCandle(open, inside[0].Open, Text(high), Text(low), inside[^1].Close, Text(inside.Length), Turnover: null, BuyVolume: null));
            }
        }

        HistoryPayment[] payments =
        [
            .. (History.Payments.TryGetValue(id, out List<HistoryPayment>? history) ? history : []).Concat(Paid.Where(paid => paid.Symbol == symbol).Select(paid => new HistoryPayment(paid.Time, Text(paid.Rate))))
                .GroupBy(payment => payment.Time).Select(same => same.First()).OrderBy(payment => payment.Time)
        ];
        var funding = new HistoryRead<HistoryPayment>([new TimeRange(Ten.AddDays(-2), end.AddSeconds(1))], payments);
        var orders = new PairOrders(PairSteps.Of(strategy, timeframe, symbol, candles, payments), Ten, end, TradeCosts.Default);
        foreach (long week in orders.Weeks)
        {
            (DateTimeOffset from, DateTimeOffset to) = orders.Span(week);
            var read = new HistoryRead<HistoryCandle>([new TimeRange(from, to)], [.. minutes.Where(minute => minute.OpenTime >= from && minute.OpenTime < to)]);
            orders.Evaluate(week, MinutePaths.Build(from, to, read, funding));
        }

        IReadOnlyList<CycleOutcome> cycles = orders.Cycles();
        return [.. orders.Outcomes().Where(order => order.Cycle is not null).Select(order => (order.Moment, cycles[order.Cycle!.Value]))];
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

    private static string CloseUp(string after, string side, string operation, decimal level, decimal? stop, decimal? target = null)
    {
        string stopNode = stop is { } distance ? $$""", { "id": "stop", "kind": "stop-loss", "inputs": { "distance": {{Text(distance)}} } }""" : "";
        string stopWires = stop is null ? "" : """, { "from": "position.position", "to": "stop.position" }, { "from": "stop.order", "to": "position.orders" }""";
        if (target is { } reach)
        {
            stopNode += $$""", { "id": "target", "kind": "take-profit", "inputs": { "distance": {{Text(reach)}} } }""";
            stopWires += """, { "from": "position.position", "to": "target.position" }, { "from": "target.order", "to": "position.orders" }""";
        }

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
