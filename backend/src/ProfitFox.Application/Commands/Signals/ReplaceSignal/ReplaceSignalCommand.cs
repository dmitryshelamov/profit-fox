using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Commands.Signals.ReplaceSignal;

/// <summary>
///     Replaces a saved signal; it must be there. A graph that differs by content makes the next version.
/// </summary>
public sealed record ReplaceSignalCommand(string Name, string? Description, JsonElement Graph, IReadOnlyDictionary<string, NodePosition> Layout)
    : IRequest<SignalInternal>;
