using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Presentation.Contracts.Nodes;
using ProfitFox.Presentation.Contracts.Nodes.Converters;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Spec signal-constructor «Каталог узлов» (task 3.1; change add-strategy-graph, task 4.5) and spec strategy-constructor
///     «Каталог узлов стратегии» (change add-strategy-constructor, task 4.4).
/// </summary>
[NonParallelizable]
public sealed class NodesTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    [Test]
    public async Task The_compare_node_has_its_pins_and_operations()
    {
        NodeKindResponse[] kinds = await Catalog();

        NodeKindResponse compare = kinds.Single(kind => kind.Kind == "compare");
        Assert.That(compare.Inputs, Is.EqualTo(new[] { new PinResponse("a", "number"), new PinResponse("b", "number") }));
        Assert.That(compare.Outputs, Is.EqualTo(new[] { new PinResponse("result", "boolean") }));
        Assert.That(compare.Settings.Single().Name, Is.EqualTo("operation"));
        Assert.That(compare.Settings.Single().Values, Is.EqualTo(new[] { "gt", "ge", "lt", "le", "eq", "ne" }));
        Assert.That((compare.Title, compare.Group), Is.EqualTo(("Сравнить", "Логика")));
    }

    [Test]
    public async Task Every_kind_of_the_catalog_has_a_title_and_a_group()
    {
        NodeKindResponse[] kinds = await Catalog();

        Assert.That(kinds.Select(kind => kind.Kind), Is.EqualTo(NodeCatalog.All.Where(kind => kind.AllowedInSignal).Select(kind => kind.Name)));
        Assert.That(NodeCatalog.All.Select(kind => NodeTitles.Of(kind.Name)), Has.None.Null, "a kind without words in NodeTitles");
        Assert.That(kinds.Select(kind => kind.Title), Has.None.Empty);
    }

    [Test]
    public async Task The_position_nodes_are_not_in_the_catalog_of_a_signal()
    {
        NodeKindResponse[] kinds = await Catalog();

        Assert.That(kinds.Select(kind => kind.Kind), Has.None.AnyOf("enter", "position", "market-order", "limit-order", "close-position", "stop-loss", "take-profit", "close-after"));
        Assert.That(kinds, Has.Length.EqualTo(25));
    }

    [Test]
    public void The_position_nodes_have_their_words_for_the_constructor_of_strategies()
    {
        Assert.That(
            new[] { "enter", "position", "market-order", "limit-order", "close-position", "stop-loss", "take-profit", "close-after" }.Select(NodeTitles.Of),
            Is.EqualTo(new (string, string)?[]
            {
                ("Вход", "Позиция"), ("Позиция", "Позиция"), ("Рыночная заявка", "Позиция"), ("Лимитная заявка", "Позиция"),
                ("Закрыть позицию", "Позиция"), ("Стоп", "Позиция"), ("Цель", "Позиция"), ("Закрыть через", "Позиция")
            }));
    }

    /// <summary>
    ///     Spec signal-constructor «Арифметические узлы в каталоге сигнала» (change add-position-node, task 2.2).
    /// </summary>
    [Test]
    public async Task The_arithmetic_is_in_the_catalog_of_a_signal()
    {
        NodeKindResponse[] kinds = await Catalog();

        NodeKindResponse multiply = kinds.Single(kind => kind.Kind == "multiply");
        Assert.That(multiply.Inputs.Select(pin => (pin.Name, pin.Type)), Is.EqualTo(new[] { ("a", "number"), ("b", "number") }));
        Assert.That(multiply.Outputs.Select(pin => (pin.Name, pin.Type)), Is.EqualTo(new[] { ("result", "number") }));
        Assert.That((multiply.Title, multiply.Group), Is.EqualTo(("Умножить", "Числа")));
        Assert.That(
            kinds.Where(kind => kind.Group == "Числа").Select(kind => (kind.Kind, kind.Title)),
            Is.EqualTo(new[]
            {
                ("add", "Сложить"), ("subtract", "Вычесть"), ("multiply", "Умножить"),
                ("divide", "Разделить"), ("min", "Меньшее"), ("max", "Большее"), ("select", "Выбрать")
            }));
    }

    [Test]
    public async Task The_pins_of_parameters_and_outputs_come_from_the_graph()
    {
        NodeKindResponse[] kinds = await Catalog();

        Assert.That(
            kinds.Where(kind => kind.PinsFromGraph is not null).Select(kind => (kind.Kind, kind.PinsFromGraph)),
            Is.EqualTo(new[] { ("parameters", (string?)"parameters"), ("outputs", "outputs") }));
        Assert.That(kinds.Single(kind => kind.Kind == "first-time").Outputs, Is.EqualTo(new[] { new PinResponse("event", "event") }));
    }

    [Test]
    public async Task The_position_nodes_in_the_catalog_of_a_strategy()
    {
        NodeKindResponse[] kinds = await Catalog("/api/nodes/strategy");

        Assert.That(kinds.Select(kind => kind.Kind), Is.SupersetOf(new[] { "enter", "position", "market-order", "limit-order", "close-position", "stop-loss", "take-profit", "close-after", "signal" }));
        Assert.That(kinds.Select(kind => kind.Kind), Has.None.EqualTo("outputs"));
        Assert.That(kinds.Select(kind => kind.Kind), Is.EqualTo(NodeCatalog.All.Where(kind => kind.Name != "outputs").Select(kind => kind.Name)));
        NodeKindResponse position = kinds.Single(kind => kind.Kind == "position");
        Assert.That(position.Inputs.Single(), Is.EqualTo(new PinResponse("orders", "order") { Many = true }), "the input that takes several wires");
        Assert.That(kinds.SelectMany(kind => kind.Inputs).Count(pin => pin.Many), Is.EqualTo(1));
        Assert.That(
            kinds.Single(kind => kind.Kind == "close-after").Inputs,
            Is.EqualTo(new[] { new PinResponse("position", "position"), new PinResponse("after", "duration") }));
        NodeKindResponse signal = kinds.Single(kind => kind.Kind == "signal");
        Assert.That((signal.Title, signal.Group, signal.PinsFromGraph), Is.EqualTo(("Сигнал", "Сигналы", (string?)"signal")));
        Assert.That(signal.Inputs, Is.Empty);
        Assert.That(signal.Outputs, Is.EqualTo(new[] { new PinResponse("event", "event") }));
        Assert.That(signal.Settings, Is.EqualTo(new[] { new SettingResponse("signal", [], null), new SettingResponse("version", [], null) }).Using<SettingResponse>(Same));
    }

    /// <summary>
    ///     Change add-strategy-backtest, task 2.3: spec strategy-constructor «Вид в списке и в редакторе» («Лимитная заявка
    ///     в категории идей»): the catalog of a category of ideas has no node of an order by a condition; that of a strategy
    ///     for a bot has every kind of a strategy.
    /// </summary>
    [Test]
    public async Task The_catalog_of_a_kind_of_strategy()
    {
        string[] idea = [.. (await Catalog("/api/nodes/strategy?kind=idea")).Select(kind => kind.Kind)];
        string[] bot = [.. (await Catalog("/api/nodes/strategy?kind=bot")).Select(kind => kind.Kind)];
        string[] all = [.. (await Catalog("/api/nodes/strategy")).Select(kind => kind.Kind)];

        Assert.That(idea, Has.None.AnyOf("market-order", "limit-order", "close-position"));
        Assert.That(idea, Is.SupersetOf(new[] { "enter", "position", "stop-loss", "take-profit", "close-after", "signal" }));
        Assert.That(idea, Is.EqualTo(all.Except(["market-order", "limit-order", "close-position"])));
        Assert.That(bot, Is.EqualTo(all));
    }

    [Test]
    public async Task An_unknown_kind_of_the_catalog_is_refused()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);

        HttpResponseMessage response = await api.Http.GetAsync("/api/nodes/strategy?kind=plan");

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task The_catalog_of_a_signal_has_no_position_nodes_and_gains_the_arithmetic()
    {
        NodeKindResponse[] kinds = await Catalog();

        Assert.That(kinds.Select(kind => kind.Kind), Has.None.AnyOf("enter", "position", "market-order", "limit-order", "close-position", "stop-loss", "take-profit", "close-after", "signal"));
        Assert.That(
            kinds.Select(kind => kind.Kind),
            Is.EqualTo(new[]
            {
                "parameters", "candle", "funding-rate", "pair-candle", "compare", "previous", "and", "or", "not", "first-time", "add", "subtract", "multiply",
                "divide", "min", "max", "select", "window", "percentile", "hold", "atr", "rsi", "zones", "candles", "outputs"
            }));
    }

    /// <summary>
    ///     Change add-setup-strategies, task 3.1: spec signal-constructor «Окно в каталоге».
    /// </summary>
    [Test]
    public async Task The_window_has_its_pins_its_operations_and_the_group_of_windows()
    {
        NodeKindResponse window = (await Catalog()).Single(kind => kind.Kind == "window");

        Assert.That(window.Inputs, Is.EqualTo(new[] { new PinResponse("value", "number"), new PinResponse("length", "number") }));
        Assert.That(window.Outputs, Is.EqualTo(new[] { new PinResponse("result", "number") }));
        Assert.That(window.Settings.Single().Name, Is.EqualTo("operation"));
        Assert.That(window.Settings.Single().Values, Is.EqualTo(new[] { "max", "min", "sum", "average", "median", "first" }));
        Assert.That((window.Title, window.Group), Is.EqualTo(("Окно", "Окна")));
    }

    /// <summary>
    ///     Change add-setup-strategies, task 3.1: spec signal-constructor «Зоны в каталоге», and the words of the other new
    ///     kinds.
    /// </summary>
    [Test]
    public async Task The_zones_have_their_pins_and_the_group_of_indicators()
    {
        NodeKindResponse[] kinds = await Catalog();

        NodeKindResponse zones = kinds.Single(kind => kind.Kind == "zones");
        Assert.That(zones.Inputs.Select(pin => (pin.Name, pin.Type)), Is.EqualTo(new[] { ("atr", "number"), ("price", "number"), ("length", "number"), ("merge", "number"), ("strength", "number") }));
        Assert.That(zones.Outputs.Select(pin => (pin.Name, pin.Type)), Is.EqualTo(new[] { ("above", "number"), ("below", "number") }));
        Assert.That((zones.Title, zones.Group), Is.EqualTo(("Зоны", "Индикаторы")));
        Assert.That(
            kinds.Where(kind => kind.Group is "Окна" or "Индикаторы" || kind.Kind == "candles").Select(kind => (kind.Kind, kind.Title, kind.Group)),
            Is.EqualTo(new[]
            {
                ("window", "Окно", "Окна"), ("percentile", "Перцентиль", "Окна"), ("hold", "Запомнить", "Окна"),
                ("atr", "ATR", "Индикаторы"), ("rsi", "RSI", "Индикаторы"), ("zones", "Зоны", "Индикаторы"), ("candles", "Срок в свечах", "Данные")
            }));
        Assert.That(kinds.Single(kind => kind.Kind == "candles").Outputs, Is.EqualTo(new[] { new PinResponse("duration", "duration") }));
    }

    /// <summary>
    ///     Change add-setup-strategies, task 3.1: spec signal-constructor «Те же узлы в стратегии».
    /// </summary>
    [Test]
    public async Task The_new_kinds_are_in_the_catalog_of_a_strategy_of_both_kinds()
    {
        string[] added = ["divide", "min", "max", "select", "window", "percentile", "hold", "atr", "rsi", "zones", "candles"];

        Assert.That((await Catalog("/api/nodes/strategy")).Select(kind => kind.Kind), Is.SupersetOf(added));
        Assert.That((await Catalog("/api/nodes/strategy?kind=idea")).Select(kind => kind.Kind), Is.SupersetOf(added));
        Assert.That((await Catalog("/api/nodes/strategy?kind=bot")).Select(kind => kind.Kind), Is.SupersetOf(added));
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 4.1: the node «свеча другой пары» in the group «Данные» with its setting.
    /// </summary>
    [Test]
    public async Task The_candle_of_another_pair_is_a_node_of_the_data_with_the_setting_symbol()
    {
        NodeKindResponse[] kinds = await Catalog();

        NodeKindResponse pair = kinds.Single(kind => kind.Kind == "pair-candle");
        Assert.That((pair.Title, pair.Group), Is.EqualTo(("Свеча пары", "Данные")));
        Assert.That(pair.Settings.Select(setting => setting.Name), Is.EqualTo(new[] { "symbol" }));
        Assert.That(pair.Outputs.Select(pin => pin.Name), Is.EqualTo(new[] { "open", "high", "low", "close", "volume" }));
    }

    private static async Task<NodeKindResponse[]> Catalog(string path = "/api/nodes")
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using var api = new BenchTestApplication(market);
        return (await api.Http.GetFromJsonAsync<NodeKindResponse[]>(path, Json))!;
    }

    private static bool Same(SettingResponse a, SettingResponse b)
    {
        return a.Name == b.Name && a.Values.SequenceEqual(b.Values) && a.Default == b.Default;
    }
}
