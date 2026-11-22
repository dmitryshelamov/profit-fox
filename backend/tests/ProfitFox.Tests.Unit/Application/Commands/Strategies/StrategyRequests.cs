using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Application.Commands.Strategies.CreateStrategy;
using ProfitFox.Application.Commands.Strategies.DeleteStrategy;
using ProfitFox.Application.Commands.Strategies.RenameStrategy;
using ProfitFox.Application.Commands.Strategies.ReplaceStrategy;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Queries.Strategies;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;
using ProfitFox.Tests.Unit.Application.Commands.Signals;

namespace ProfitFox.Tests.Unit.Application.Commands.Strategies;

/// <summary>
///     The requests of the strategy library sent straight to their handlers over repositories in memory that the signal
///     requests of the test share, and the strategies the tests save.
/// </summary>
internal sealed class StrategyRequests
{
    public static readonly Dictionary<string, NodePosition> NoLayout = [];

    public MemorySignalRepository SignalStore { get; } = new MemorySignalRepository();

    public MemoryStrategyRepository Store { get; } = new MemoryStrategyRepository();

    /// <summary>
    ///     The signal requests over the same repositories.
    /// </summary>
    public SignalRequests Signals => new SignalRequests(SignalStore, Store);

    /// <summary>
    ///     Saves <c>funding-high</c> with the graph of H0001 as its version <paramref name="version" />.
    /// </summary>
    public async Task<SignalDocument> SaveSignal(string name = "funding-high", int version = 1)
    {
        SignalDocument signal = SignalDocumentGenerator.Generate(name, version: version);
        return await SignalStore.CreateAsync(signal, CancellationToken.None);
    }

    public Task<StrategyInternal> Create(string name, JsonElement graph, CancellationToken cancellationToken)
    {
        return new CreateStrategyCommandHandler(Store, SignalStore).Handle(new CreateStrategyCommand(name, null, graph, NoLayout), cancellationToken);
    }

    public Task<StrategyInternal> Replace(string name, JsonElement graph, CancellationToken cancellationToken)
    {
        return new ReplaceStrategyCommandHandler(Store, SignalStore).Handle(new ReplaceStrategyCommand(name, null, graph, NoLayout), cancellationToken);
    }

    public Task<StrategyInternal> Rename(string name, string to, CancellationToken cancellationToken)
    {
        return new RenameStrategyCommandHandler(Store, SignalStore).Handle(new RenameStrategyCommand(name, to), cancellationToken);
    }

    public Task Delete(string name, CancellationToken cancellationToken)
    {
        return new DeleteStrategyCommandHandler(Store).Handle(new DeleteStrategyCommand(name), cancellationToken);
    }

    public Task<StrategyInternal> Read(string name, CancellationToken cancellationToken)
    {
        return new GetStrategyQueryHandler(Store, SignalStore).Handle(new GetStrategyQuery(name), cancellationToken);
    }

    public Task<IReadOnlyList<StrategyEntryInternal>> List(CancellationToken cancellationToken)
    {
        return new GetStrategiesQueryHandler(Store, SignalStore).Handle(new GetStrategiesQuery(), cancellationToken);
    }

    public Task<StrategyCheckInternal> Check(JsonElement graph, CancellationToken cancellationToken)
    {
        return new CheckStrategyGraphQueryHandler(SignalStore).Handle(new CheckStrategyGraphQuery(graph), cancellationToken);
    }

    /// <summary>
    ///     <c>funding-short</c>: the node <c>s</c> on <paramref name="version" /> of <paramref name="signal" />, a short
    ///     <c>enter</c> on its event and a <c>close-after</c> of 4h.
    /// </summary>
    public static JsonObject Strategy(Guid signal, int version = 1)
    {
        return JsonNode.Parse(
            $$"""
              {
                "formatVersion": 1,
                "nodes": [
                  { "id": "s", "kind": "signal", "settings": { "signal": "{{signal}}", "version": "{{version}}" } },
                  { "id": "enter", "kind": "enter", "settings": { "side": "short" } },
                  { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
                ],
                "wires": [{ "from": "s.event", "to": "enter.when" }]
              }
              """)!.AsObject();
    }

    public static JsonElement Element(JsonNode node)
    {
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }
}
