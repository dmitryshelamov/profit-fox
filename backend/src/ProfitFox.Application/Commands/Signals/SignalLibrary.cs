using System.Text.Json;

using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Signals;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Application.Commands.Signals;

/// <summary>
///     The saved signals of the constructor (spec signal-constructor), kept by <see cref="ISignalStore" />. A graph is
///     written only when it reads as a signal, and a name only when it follows <see cref="SignalNames" />; a refused
///     request leaves the store as it was.
/// </summary>
public sealed class SignalLibrary(ISignalStore store)
{
    /// <summary>
    ///     Every saved signal in the order of its name; one that does not read stays in the list with its errors.
    /// </summary>
    public async Task<IReadOnlyList<SignalEntry>> List(CancellationToken cancellation)
    {
        List<SignalEntry> entries = [];
        foreach (string name in (await store.Names(cancellation)).Order(StringComparer.Ordinal))
        {
            entries.Add(await Entry(name, cancellation));
        }

        return entries;
    }

    public async Task<SignalDocument> Read(string name, CancellationToken cancellation)
    {
        if (!SignalNames.IsValid(name) || await store.Read(name, cancellation) is not { } read)
        {
            throw NotFound(name);
        }

        return read.Signal ?? throw new SignalLibraryException(SignalProblem.Unreadable, string.Join("; ", read.Errors));
    }

    /// <summary>
    ///     Writes a new signal; a name that is taken is refused.
    /// </summary>
    public async Task<SignalDocument> Create(
        string name,
        string? description,
        JsonElement graph,
        IReadOnlyDictionary<string, NodePosition> layout,
        CancellationToken cancellation)
    {
        SignalDocument signal = Checked(name, description, graph, layout);
        if (await store.Exists(name, cancellation))
        {
            throw Taken(name);
        }

        return await store.Create(signal, cancellation);
    }

    /// <summary>
    ///     Replaces a saved signal; it must be there. A graph that differs by content makes the next version.
    /// </summary>
    public async Task<SignalDocument> Replace(
        string name,
        string? description,
        JsonElement graph,
        IReadOnlyDictionary<string, NodePosition> layout,
        CancellationToken cancellation)
    {
        if (!SignalNames.IsValid(name) || !await store.Exists(name, cancellation))
        {
            throw NotFound(name);
        }

        return await store.Replace(Checked(name, description, graph, layout), cancellation);
    }

    public async Task<SignalDocument> Rename(string name, string to, CancellationToken cancellation)
    {
        SignalDocument signal = await Read(name, cancellation);
        if (!SignalNames.IsValid(to))
        {
            throw new SignalLibraryException(SignalProblem.Invalid, $"{to} is not a signal name", [NameError(to)]);
        }

        if (to == name)
        {
            return signal;
        }

        if (await store.Exists(to, cancellation))
        {
            throw Taken(to);
        }

        await store.Rename(name, to, cancellation);
        return signal with { Name = to };
    }

    public async Task Delete(string name, CancellationToken cancellation)
    {
        if (!SignalNames.IsValid(name) || !await store.Exists(name, cancellation))
        {
            throw NotFound(name);
        }

        await store.Delete(name, cancellation);
    }

    private async Task<SignalEntry> Entry(string name, CancellationToken cancellation)
    {
        try
        {
            SignalDocument signal = await Read(name, cancellation);
            return new SignalEntry(signal.Name, signal.Description, signal.Version, Error: null);
        }
        catch (SignalLibraryException exception)
        {
            return new SignalEntry(name, Description: null, Version: 0, exception.Message);
        }
    }

    private static SignalDocument Checked(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout)
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
        var ids = definition.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var kept = layout.Where(position => ids.Contains(position.Key)).ToDictionary(StringComparer.Ordinal);
        return new SignalDocument(name, string.IsNullOrWhiteSpace(description) ? null : description, definition, kept);
    }

    private static GraphError NameError(string name)
    {
        return new GraphError(
            GraphErrorCode.InvalidField,
            $"{name} is not a signal name: lower-case latin letters and digits joined by single dashes, at most {SignalNames.MaxLength} characters")
        {
            Field = "name"
        };
    }

    private static SignalLibraryException NotFound(string name)
    {
        return new SignalLibraryException(SignalProblem.NotFound, $"there is no signal {name}");
    }

    private static SignalLibraryException Taken(string name)
    {
        return new SignalLibraryException(SignalProblem.NameTaken, $"the name {name} is taken");
    }
}
