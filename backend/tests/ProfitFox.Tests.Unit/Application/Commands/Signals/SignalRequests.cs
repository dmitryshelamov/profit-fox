using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitFox.Application.Commands.Signals.CreateSignal;
using ProfitFox.Application.Commands.Signals.DeleteSignal;
using ProfitFox.Application.Commands.Signals.RenameSignal;
using ProfitFox.Application.Commands.Signals.ReplaceSignal;
using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Queries.Signals;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Tests.Unit.Domain.Services.Hypotheses;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     The requests of the signal library sent straight to their handlers over one repository in memory, and the graph of H0001
///     the tests save.
/// </summary>
internal sealed class SignalRequests
{
    public static readonly Dictionary<string, NodePosition> NoLayout = [];

    private readonly MemorySignalRepository _signals = new MemorySignalRepository();

    public Task<SignalInternal> Create(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout, CancellationToken cancellationToken)
    {
        return new CreateSignalCommandHandler(_signals).Handle(new CreateSignalCommand(name, description, graph, layout), cancellationToken);
    }

    public Task<SignalInternal> Replace(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout, CancellationToken cancellationToken)
    {
        return new ReplaceSignalCommandHandler(_signals).Handle(new ReplaceSignalCommand(name, description, graph, layout), cancellationToken);
    }

    public Task<SignalInternal> Rename(string name, string to, CancellationToken cancellationToken)
    {
        return new RenameSignalCommandHandler(_signals).Handle(new RenameSignalCommand(name, to), cancellationToken);
    }

    public Task Delete(string name, CancellationToken cancellationToken)
    {
        return new DeleteSignalCommandHandler(_signals).Handle(new DeleteSignalCommand(name), cancellationToken);
    }

    public Task<SignalInternal> Read(string name, CancellationToken cancellationToken)
    {
        return new GetSignalQueryHandler(_signals).Handle(new GetSignalQuery(name), cancellationToken);
    }

    public Task<IReadOnlyList<SignalEntryInternal>> List(CancellationToken cancellationToken)
    {
        return new GetSignalsQueryHandler(_signals).Handle(new GetSignalsQuery(), cancellationToken);
    }

    public static JsonNode H0001GraphNode()
    {
        return JsonNode.Parse(HypothesisReaderTests.H0001Text)!["signal"]!.DeepClone();
    }

    public static JsonElement H0001Graph()
    {
        return Element(H0001GraphNode());
    }

    public static JsonElement Element(JsonNode node)
    {
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }
}
