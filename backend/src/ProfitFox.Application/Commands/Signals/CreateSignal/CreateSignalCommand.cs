using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Commands.Signals.CreateSignal;

/// <summary>
///     Writes a new signal; a name that is taken is refused.
/// </summary>
public sealed record CreateSignalCommand(string Name, string? Description, JsonElement Graph, IReadOnlyDictionary<string, NodePosition> Layout)
    : IRequest<SignalInternal>;
