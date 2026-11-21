using System.Text.Json;

using ProfitFox.Application.Commands.Base;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Commands.Signals.Base;

/// <summary>
///     What the requests of the signal library (spec signal-constructor) check the same way. A graph is written only when it reads
///     as a signal, and a name only when it follows <see cref="SignalNames" />; a refused request leaves the repository as it was.
/// </summary>
internal static class SignalChecks
{
    /// <summary>
    ///     The latest version of a saved signal.
    /// </summary>
    /// <exception cref="SignalLibraryException">There is no such signal, or it does not read.</exception>
    public static async Task<SignalDocument> ReadAsync(ISignalRepository signals, string name, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(name) || await signals.ReadAsync(name, cancellationToken) is not { } read)
        {
            throw NotFound(name);
        }

        return read.Signal ?? throw new SignalLibraryException(SignalProblem.Unreadable, string.Join("; ", read.Errors));
    }

    /// <summary>
    ///     The signal of a request, with the positions of the nodes of its graph only.
    /// </summary>
    /// <exception cref="SignalLibraryException">The name is not allowed or the graph has errors.</exception>
    public static SignalDocument Checked(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout)
    {
        List<GraphError> errors = [];
        if (!SignalNames.IsValid(name))
        {
            errors.Add(NameError(name));
        }

        ReadResult<Signal> read = StrategyGraph.ReadSignal(graph);
        errors.AddRange(read.Errors);
        if (errors.Count > 0 || read.Value is null)
        {
            throw new SignalLibraryException(SignalProblem.Invalid, string.Join("; ", errors), errors);
        }

        GraphDefinition definition = read.Value.Graph;
        return SignalDocument.Create(name, string.IsNullOrWhiteSpace(description) ? null : description, definition, GraphLayout.Of(definition, layout));
    }

    public static GraphError NameError(string name)
    {
        return DocumentNames.Error(name, "signal");
    }

    public static SignalLibraryException NotFound(string name)
    {
        return new SignalLibraryException(SignalProblem.NotFound, $"there is no signal {name}");
    }

    public static SignalLibraryException InUse(string name, IReadOnlyList<string> strategies)
    {
        return new SignalLibraryException(
            SignalProblem.InUse,
            $"the signal {name} is used by the strategies {string.Join(", ", strategies)}",
            strategies: strategies);
    }

    public static SignalLibraryException Taken(string name)
    {
        return new SignalLibraryException(SignalProblem.NameTaken, $"the name {name} is taken");
    }
}
