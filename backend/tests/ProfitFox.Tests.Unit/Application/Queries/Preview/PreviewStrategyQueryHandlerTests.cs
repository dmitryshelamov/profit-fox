using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.Preview;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;
using ProfitFox.Tests.Unit.Application.Commands.Signals;
using ProfitFox.Tests.Unit.Application.Commands.Strategies;
using ProfitFox.Tests.Unit.Application.Queries.MarketData;

namespace ProfitFox.Tests.Unit.Application.Queries.Preview;

/// <summary>
///     Spec strategy-preview «Предпросмотр стратегии на паре», «Ошибки запроса предпросмотра стратегии», «Только разведка» and
///     «Повторный предпросмотр стратегии» (change add-strategy-constructor, task 5.2), over a fake history: 4h candles from
///     2026-01-05 and funding every 8 hours whose rate reaches 0.0006 at 08:00 of every fifth day.
/// </summary>
public sealed class PreviewStrategyQueryHandlerTests
{
    private static readonly Instrument Pair = new Instrument("instrument-1", "BTCUSDT");
    private readonly CancellationToken _none = CancellationToken.None;
    private FakeHistorySource _history = null!;
    private PreviewHistoryCache _cache = null!;
    private MemorySignalRepository _signals = null!;
    private SignalDocument _signal = null!;

    [SetUp]
    public async Task Create()
    {
        _history = new FakeHistorySource { Rate = time => time.Hour == 8 && time.DayOfYear % 5 == 0 ? "0.0006" : "0.0001" };
        _cache = new PreviewHistoryCache();
        _signals = new MemorySignalRepository();
        _signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
    }

    [Test]
    public async Task The_strategy_of_H0001_on_a_pair()
    {
        StrategyPreviewInternal preview = (await Preview(StrategyRequests.Strategy(_signal.Id))).Preview!;

        IReadOnlyList<DateTimeOffset> events = PairEvents.Of(
            StrategyGraph.ReadSignal(StrategyGraph.Write(_signal.Graph)).Value!,
            TimeSpan.FromHours(4),
            Pair.Symbol,
            (await _history.ReadCandles(Pair.Id, TimeSpan.FromHours(4), Periods.DataStart, Periods.ExplorationEnd, _none)).Items,
            (await _history.ReadFunding(Pair.Id, Periods.DataStart, Periods.ExplorationEnd, _none)).Items);
        Assert.That(events, Has.Count.GreaterThan(5));
        Assert.That(preview.Orders.Select(order => order.Moment), Is.EqualTo(events), "an order at every event of the signal");
        Assert.That(preview.Orders.Select(order => order.Side), Has.All.EqualTo(EntrySide.Short));
        // The history has orders in about 19 weeks: the trades of the last 8 are computed (spec «Предел минутной истории»).
        DateTimeOffset from = preview.ComputedFrom!.Value;
        Assert.That(from.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        OrderOutcome[] earlier = [.. preview.Orders.Where(order => order.Moment < from)];
        OrderOutcome[] computed = [.. preview.Orders.Where(order => order.Moment >= from)];
        Assert.That(earlier, Is.Not.Empty);
        Assert.That(computed, Is.Not.Empty);
        Assert.That(earlier.Select(order => order.Result), Has.All.EqualTo(OrderResult.NotComputed));
        Assert.That(computed.Select(order => order.Result), Has.All.EqualTo(OrderResult.Opened));
        Assert.That(computed.Select(order => order.Trade!.ExitTime - order.Moment), Has.All.EqualTo((TimeSpan?)TimeSpan.FromHours(4)));
        Assert.That(preview.Candles, Is.Not.Empty);
        Assert.That(preview.Candles[^1].OpenTime, Is.LessThan(Periods.ExplorationEnd));
        Assert.That(preview.PeriodEnd, Is.EqualTo(Periods.ExplorationEnd));
    }

    [Test]
    public async Task No_read_goes_past_the_end_of_exploration()
    {
        await Preview(StrategyRequests.Strategy(_signal.Id));

        Assert.That(_history.Reads.Count(read => read.Feed == "candles"), Is.GreaterThan(1), "the minutes of the weeks are read");
        Assert.That(_history.Reads.Where(read => read.Feed == "candles").Select(read => read.To), Has.All.LessThanOrEqualTo(Periods.ExplorationEnd));
        // A payment exactly at the end belongs to the trades that end there, as in the preview of a signal and the runs.
        Assert.That(_history.Reads.Where(read => read.Feed == "funding").Select(read => read.To), Has.All.LessThanOrEqualTo(Periods.ExplorationEnd.AddSeconds(1)));
    }

    [Test]
    public async Task A_pair_without_orders()
    {
        _history = new FakeHistorySource();

        StrategyPreviewInternal preview = (await Preview(StrategyRequests.Strategy(_signal.Id))).Preview!;

        Assert.That(preview.Candles, Is.Not.Empty);
        Assert.That(preview.Orders, Is.Empty);
        Assert.That(_history.Reads.Count(read => read.Feed == "candles"), Is.EqualTo(1), "no minutes without orders");
    }

    [Test]
    public async Task An_error_of_the_graph_and_an_unknown_pair()
    {
        JsonObject graph = StrategyRequests.Strategy(_signal.Id);
        graph["nodes"]!.AsArray().RemoveAt(2);

        StrategyPreviewOutcomeInternal outcome = await Preview(graph, instrument: "NOPEUSDT");

        Assert.That(outcome.Preview, Is.Null);
        Assert.That(outcome.Errors.Select(error => error.Code), Is.EquivalentTo(new[] { "StrategyCloseAfterMissing", "UnknownInstrument" }));
        Assert.That(_history.Reads, Is.Empty);
    }

    [Test]
    public async Task A_wrong_timeframe_and_no_graph_come_in_one_answer()
    {
        StrategyPreviewOutcomeInternal outcome = await new PreviewStrategyQueryHandler(_history, new Catalog(), _cache, _signals)
            .Handle(new PreviewStrategyQuery(Pair.Symbol, "2h", Strategy: null), _none);

        Assert.That(outcome.Errors.Select(error => error.Field), Is.EquivalentTo(new[] { "timeframe", "strategy" }));
        Assert.That(_history.Reads, Is.Empty);
    }

    [Test]
    public async Task A_changed_stop_and_a_second_preview()
    {
        JsonObject graph = StrategyRequests.Strategy(_signal.Id);
        graph["nodes"]!.AsArray().Add(JsonNode.Parse("""{ "id": "stop", "kind": "stop-loss", "inputs": { "distance": 0.02 } }"""));
        await Preview(graph);
        int candles = _history.Reads.Count(read => read.Feed == "candles" && read.To - read.From > TimeSpan.FromDays(30));
        int funding = _history.Reads.Count(read => read.Feed == "funding");
        graph["nodes"]![3]!["inputs"]!["distance"] = 0.03;

        StrategyPreviewInternal second = (await Preview(graph)).Preview!;

        Assert.That(second.Orders, Is.Not.Empty);
        Assert.That(_history.Reads.Count(read => read.Feed == "candles" && read.To - read.From > TimeSpan.FromDays(30)), Is.EqualTo(candles), "the candles of the timeframe come from memory");
        Assert.That(_history.Reads.Count(read => read.Feed == "funding"), Is.EqualTo(funding), "the funding comes from memory");
    }

    private Task<StrategyPreviewOutcomeInternal> Preview(JsonObject graph, string instrument = "BTCUSDT")
    {
        JsonElement element = StrategyRequests.Element(graph);
        return new PreviewStrategyQueryHandler(_history, new Catalog(), _cache, _signals).Handle(new PreviewStrategyQuery(instrument, "4h", element), _none);
    }

    private sealed class Catalog : IInstrumentCatalog
    {
        public Task<IReadOnlyList<Instrument>> LinearUsdtPerpetuals(CancellationToken cancellation)
        {
            return Task.FromResult<IReadOnlyList<Instrument>>([Pair]);
        }
    }
}
