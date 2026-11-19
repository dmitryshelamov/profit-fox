using System.Net.Http.Json;
using System.Text.Json;
using ProfitFox.Bench.Presentation.Nodes;
using ProfitFox.Engine.Strategy.Nodes;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Presentation;

/// <summary>
///     Spec signal-constructor «Каталог узлов» (task 3.1).
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

        Assert.That(kinds.Select(kind => kind.Kind), Is.EqualTo(NodeCatalog.All.Select(kind => kind.Name)));
        Assert.That(NodeCatalog.All.Select(kind => NodeTitles.Of(kind.Name)), Has.None.Null, "a kind without words in NodeTitles");
        Assert.That(kinds.Select(kind => kind.Title), Has.None.Empty);
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

    private static async Task<NodeKindResponse[]> Catalog()
    {
        await using MarketDataServer market = await MarketDataServer.Start();
        await using BenchPresentationServer api = await BenchPresentationServer.Start(market, Path.GetTempPath());
        return (await api.Http.GetFromJsonAsync<NodeKindResponse[]>("/api/nodes", Json))!;
    }
}
