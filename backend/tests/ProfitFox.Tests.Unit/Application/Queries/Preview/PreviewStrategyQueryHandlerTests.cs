using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.Preview;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Positions;
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
        Assert.That(preview.Orders.Select(order => order.Side), Has.All.EqualTo(OrderSide.Short));
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
        StrategyRequests.RemoveCloseAfter(graph);

        StrategyPreviewOutcomeInternal outcome = await Preview(graph, instrument: "NOPEUSDT");

        Assert.That(outcome.Preview, Is.Null);
        Assert.That(outcome.Errors.Select(error => error.Code), Is.EquivalentTo(new[] { "StrategyExitMissing", "UnknownInstrument" }));
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
        graph["wires"]!.AsArray().Add(JsonNode.Parse("""{ "from": "position.position", "to": "stop.position" }"""));
        graph["wires"]!.AsArray().Add(JsonNode.Parse("""{ "from": "stop.order", "to": "position.orders" }"""));
        await Preview(graph);
        int candles = _history.Reads.Count(read => read.Feed == "candles" && read.To - read.From > TimeSpan.FromDays(30));
        int funding = _history.Reads.Count(read => read.Feed == "funding");
        graph["nodes"]![4]!["inputs"]!["distance"] = 0.03;

        StrategyPreviewInternal second = (await Preview(graph)).Preview!;

        Assert.That(second.Orders, Is.Not.Empty);
        Assert.That(_history.Reads.Count(read => read.Feed == "candles" && read.To - read.From > TimeSpan.FromDays(30)), Is.EqualTo(candles), "the candles of the timeframe come from memory");
        Assert.That(_history.Reads.Count(read => read.Feed == "funding"), Is.EqualTo(funding), "the funding comes from memory");
    }

    /// <summary>
    ///     Change add-position-node, task 7.1: spec strategy-preview «Циклы в ответе предпросмотра» («Цикл H0001») and «Исход
    ///     заявки» («Выход по сроку»).
    /// </summary>
    [Test]
    public async Task Each_order_of_H0001_opens_a_cycle_of_an_entry_and_an_exit_by_its_time()
    {
        StrategyPreviewInternal preview = (await Preview(StrategyRequests.Strategy(_signal.Id))).Preview!;

        OrderOutcome[] filled = [.. preview.Orders.Where(order => order.Result == OrderResult.Filled)];
        Assert.That(filled, Is.Not.Empty);
        Assert.That(preview.Cycles, Has.Count.EqualTo(filled.Length));
        foreach (OrderOutcome order in filled)
        {
            CycleOutcome cycle = preview.Cycles[order.Cycle!.Value];
            Assert.That(cycle.Fills.Select(fill => fill.Action), Is.EqualTo(new[] { FillAction.Entry, FillAction.Exit }));
            Assert.That((cycle.Entries, cycle.MaxInvested, cycle.ExitReason, cycle.Open), Is.EqualTo((1, 100.0, CycleExit.Time, false)));
            Assert.That(cycle.Fills[^1].Moment, Is.EqualTo(order.Moment + TimeSpan.FromHours(4)));
            Assert.That(cycle.Net, Is.EqualTo(order.Trade!.Net), "the fraction is the result of the trade");
            Assert.That(cycle.NetAmount, Is.EqualTo(order.Trade.Net * 100));
            Assert.That((order.Node, order.Price, order.Amount), Is.EqualTo(((string?)"enter", order.Trade.EntryPrice, (double?)100)));
        }
    }

    /// <summary>
    ///     Spec strategy-preview «Исход заявки» («Пропущенная заявка»): with a time of 6 days the event 5 days later finds the
    ///     position open.
    /// </summary>
    [Test]
    public async Task An_order_while_the_position_is_open_is_skipped_and_has_no_cycle()
    {
        JsonObject graph = StrategyRequests.Strategy(_signal.Id);
        graph["nodes"]!.AsArray().Single(node => (string)node!["id"]! == "close-after")!["inputs"]!["after"] = "6d";

        StrategyPreviewInternal preview = (await Preview(graph)).Preview!;

        OrderOutcome[] skipped = [.. preview.Orders.Where(order => order.Result == OrderResult.Skipped)];
        Assert.That(skipped, Is.Not.Empty);
        Assert.That(skipped.Select(order => (order.Cycle, order.Price)), Has.All.EqualTo(((int?)null, (double?)null)));
    }

    /// <summary>
    ///     Change add-position-node, task 7.2: spec strategy-preview «Предел минутной истории» («Лесенка»), «Только разведка»
    ///     («Лесенка у конца разведки»), «Исход заявки» («Докупка в списке заявок», «Переставленная заявка») and «Циклы в ответе
    ///     предпросмотра» («Цикл лесенки», «Незакрытый цикл»). In the window the price is 100, dips to 98.5 in the second hour
    ///     (the averaging buy at 99), stands at 101 from the fourth (the sale above the new average and the next entry) and
    ///     falls to 100.5 on the last day, so the last cycle stays open.
    /// </summary>
    [Test]
    public async Task The_ladder_over_the_last_eight_weeks_of_exploration()
    {
        DateTimeOffset window = Periods.ExplorationEnd - TimeSpan.FromDays(56);
        _history = new FakeHistorySource { Price = open => Ladder(open, window) };

        StrategyPreviewInternal preview = (await Preview(JsonNode.Parse(Domain.Services.Graph.Graphs.Ladder)!.AsObject())).Preview!;

        Assert.That(preview.ComputedFrom, Is.EqualTo(window));
        Assert.That(preview.Orders.Select(order => order.Moment), Has.All.GreaterThanOrEqualTo(window), "no order before the window");
        Assert.That(_history.Reads.Where(read => read.Feed == "candles").Select(read => read.To), Has.All.LessThanOrEqualTo(Periods.ExplorationEnd));
        Assert.That(_history.Reads.Where(read => read.Feed == "candles" && read.To - read.From <= TimeSpan.FromDays(60)).Select(read => read.From), Has.All.EqualTo(window));

        CycleOutcome first = preview.Cycles[0];
        Assert.That(first.Fills.Select(fill => (fill.Moment, fill.Action)), Is.EqualTo(new[]
        {
            (window, FillAction.Entry), (window.AddHours(1), FillAction.Increase), (window.AddHours(3), FillAction.Exit)
        }));
        Assert.That((first.Entries, first.ExitReason), Is.EqualTo((2, CycleExit.LimitOrder)));
        OrderOutcome buy = preview.Orders.Single(order => order.Node == "buy");
        Assert.That((buy.Result, buy.Side, buy.Amount, buy.Cycle), Is.EqualTo((OrderResult.Filled, OrderSide.Buy, (double?)100, (int?)0)));
        Assert.That(buy.Price, Is.EqualTo(100 * (1 + 0.0005) * 0.99).Within(1e-9), "1 % below the entry, which filled worse than 100 by the slippage");
        Assert.That(preview.Orders.Count(order => order.Node == "sell"), Is.EqualTo(1), "the sale was placed again after the buy and filled once: one row");

        CycleOutcome last = preview.Cycles[^1];
        Assert.That((preview.Cycles.Count, last.Open, last.ExitReason), Is.EqualTo((2, true, CycleExit.None)));
        Assert.That(last.Fills.Single().Moment, Is.EqualTo(window.AddHours(3).AddMinutes(1)));
    }

    /// <summary>
    ///     Spec strategy-preview «Ставки комиссии в запросе» and spec strategy-position «Издержки цикла» («Комиссия лесенки»).
    /// </summary>
    [Test]
    public async Task The_fees_of_a_preview_are_those_of_Bybit_unless_the_request_gives_them()
    {
        DateTimeOffset window = Periods.ExplorationEnd - TimeSpan.FromDays(56);
        _history = new FakeHistorySource { Price = open => Ladder(open, window) };
        JsonObject ladder = JsonNode.Parse(Domain.Services.Graph.Graphs.Ladder)!.AsObject();

        CycleOutcome byDefault = (await Preview(ladder)).Preview!.Cycles[0];
        CycleOutcome freeMaker = (await Preview(ladder, makerFee: 0)).Preview!.Cycles[0];

        double sale = byDefault.Fills[^1].Amount;
        Assert.That(byDefault.Fees * byDefault.MaxInvested, Is.EqualTo(100 * 0.00055 + (100 + sale) * 0.0002).Within(1e-9), "the market entry pays the taker, the limit fills the maker");
        Assert.That(freeMaker.Fees * freeMaker.MaxInvested, Is.EqualTo(100 * 0.00055).Within(1e-12));
    }

    [Test]
    public async Task A_fee_out_of_its_bounds_is_refused_before_any_history_is_read()
    {
        StrategyPreviewOutcomeInternal outcome = await Preview(StrategyRequests.Strategy(_signal.Id), takerFee: -0.001);

        PreviewErrorInternal error = outcome.Errors.Single();
        Assert.That((error.Code, error.Field), Is.EqualTo(("InvalidFee", (string?)"takerFee")));
        Assert.That(error.Reason, Does.Contain("ставка тейкера"));
        Assert.That(_history.Reads, Is.Empty);
    }

    private static (double Open, double High, double Low, double Close) Ladder(DateTimeOffset open, DateTimeOffset window)
    {
        if (open >= window.AddHours(1) && open < window.AddHours(1).AddMinutes(1))
        {
            return (99, 99.2, 98.5, 98.8);
        }

        if (open >= window.AddHours(3) && open < Periods.ExplorationEnd - TimeSpan.FromDays(1))
        {
            return (101, 101, 101, 101);
        }

        if (open >= Periods.ExplorationEnd - TimeSpan.FromDays(1))
        {
            return (100.5, 100.5, 100.5, 100.5);
        }

        return (100, 100, 100, 100);
    }

    private Task<StrategyPreviewOutcomeInternal> Preview(JsonObject graph, string instrument = "BTCUSDT", double? takerFee = null, double? makerFee = null)
    {
        JsonElement element = StrategyRequests.Element(graph);
        return new PreviewStrategyQueryHandler(_history, new Catalog(), _cache, _signals).Handle(new PreviewStrategyQuery(instrument, "4h", element, takerFee, makerFee), _none);
    }

    private sealed class Catalog : IInstrumentCatalog
    {
        public Task<IReadOnlyList<Instrument>> LinearUsdtPerpetuals(CancellationToken cancellation)
        {
            return Task.FromResult<IReadOnlyList<Instrument>>([Pair]);
        }
    }
}
