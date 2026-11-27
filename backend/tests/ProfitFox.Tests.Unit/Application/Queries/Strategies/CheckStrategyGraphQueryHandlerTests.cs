using System.Text.Json.Nodes;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.Strategies;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Tests.Unit.Application.Commands.Strategies;

namespace ProfitFox.Tests.Unit.Application.Queries.Strategies;

/// <summary>
///     Spec strategy-constructor «Проверка графа стратегии без истории» (change add-strategy-constructor, task 4.2): every
///     error of a graph checked as a strategy, and for each signal node what it points at. The check reads no history: its
///     handler has no port of the market data.
/// </summary>
public sealed class CheckStrategyGraphQueryHandlerTests
{
    private readonly CancellationToken _none = CancellationToken.None;
    private StrategyRequests _strategies = null!;

    [SetUp]
    public void Create()
    {
        _strategies = new StrategyRequests();
    }

    [Test]
    public void The_check_has_no_port_of_the_market_data()
    {
        Type[] ports = [.. typeof(CheckStrategyGraphQueryHandler).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType)];

        Assert.That(ports, Has.None.EqualTo(typeof(IHistorySource)).And.None.EqualTo(typeof(IInstrumentCatalog)));
    }

    [Test]
    public async Task A_graph_without_errors()
    {
        SignalDocument signal = await _strategies.SaveSignal();

        StrategyCheckInternal check = await _strategies.Check(StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);

        Assert.That(check.Errors, Is.Empty);
        SignalUseInternal use = check.Signals.Single();
        Assert.That((use.Node, use.Signal, use.Name, use.Version, use.LatestVersion), Is.EqualTo(("s", signal.Id, "funding-high", 1, (int?)1)));
        ParameterDefinition threshold = use.Parameters.Single();
        Assert.That((threshold.Name, threshold.Type, threshold.Value), Is.EqualTo(("threshold", PinType.Number, GraphValue.FromNumber(0.0005m))));
    }

    [Test]
    public async Task An_enter_without_a_side()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        JsonObject graph = StrategyRequests.Strategy(signal.Id);
        graph["nodes"]![1]!.AsObject().Remove("settings");

        StrategyCheckInternal check = await _strategies.Check(StrategyRequests.Element(graph), _none);

        GraphError error = check.Errors.Single();
        Assert.That((error.Code, error.Node, error.Setting), Is.EqualTo((GraphErrorCode.MissingSetting, "enter", "side")));
    }

    [Test]
    public async Task No_such_version_of_the_signal()
    {
        SignalDocument signal = await _strategies.SaveSignal(version: 2);
        JsonObject graph = StrategyRequests.Strategy(signal.Id, version: 7);
        StrategyRequests.RemoveCloseAfter(graph);

        StrategyCheckInternal check = await _strategies.Check(StrategyRequests.Element(graph), _none);

        Assert.That(check.Errors.Select(error => (error.Code, error.Node)), Is.EquivalentTo(new[]
        {
            (GraphErrorCode.UnknownSignal, (string?)"s"), (GraphErrorCode.StrategyExitMissing, null)
        }));
        SignalUseInternal use = check.Signals.Single();
        Assert.That((use.Name, use.Version, use.LatestVersion, use.Parameters.Count), Is.EqualTo(("funding-high", 7, (int?)2, 0)));
    }

    [Test]
    public async Task A_graph_of_the_wrong_form_has_only_the_errors_of_its_form()
    {
        StrategyCheckInternal check = await _strategies.Check(StrategyRequests.Element(JsonNode.Parse("""{ "formatVersion": 2, "nodes": [] }""")!), _none);

        Assert.That(check.Errors.Single().Code, Is.EqualTo(GraphErrorCode.UnsupportedFormatVersion));
        Assert.That(check.Signals, Is.Empty);
    }
}
