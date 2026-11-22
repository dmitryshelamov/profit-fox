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
        Assert.That(kinds, Has.Length.EqualTo(13));
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
            Is.EqualTo(new[] { ("add", "Сложить"), ("subtract", "Вычесть"), ("multiply", "Умножить") }));
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
        NodeKindResponse signal = kinds.Single(kind => kind.Kind == "signal");
        Assert.That((signal.Title, signal.Group, signal.PinsFromGraph), Is.EqualTo(("Сигнал", "Сигналы", (string?)"signal")));
        Assert.That(signal.Inputs, Is.Empty);
        Assert.That(signal.Outputs, Is.EqualTo(new[] { new PinResponse("event", "event") }));
        Assert.That(signal.Settings, Is.EqualTo(new[] { new SettingResponse("signal", [], null), new SettingResponse("version", [], null) }).Using<SettingResponse>(Same));
    }

    [Test]
    public async Task The_catalog_of_a_signal_has_no_position_nodes_and_gains_the_arithmetic()
    {
        NodeKindResponse[] kinds = await Catalog();

        Assert.That(kinds.Select(kind => kind.Kind), Has.None.AnyOf("enter", "position", "market-order", "limit-order", "close-position", "stop-loss", "take-profit", "close-after", "signal"));
        Assert.That(
            kinds.Select(kind => kind.Kind),
            Is.EqualTo(new[] { "parameters", "candle", "funding-rate", "compare", "previous", "and", "or", "not", "first-time", "add", "subtract", "multiply", "outputs" }));
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
