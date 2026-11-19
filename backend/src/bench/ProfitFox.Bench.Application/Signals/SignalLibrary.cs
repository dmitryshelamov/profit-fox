using System.Text.Json;
using ProfitFox.Bench.Domain.Signals;
using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Bench.Application.Signals;

/// <summary>
///     The saved signals of the constructor (spec signal-constructor), kept by <see cref="ISignalStore" />. A graph is
///     written only when it reads as a signal, and a name only when it follows <see cref="SignalNames" />; a refused
///     request leaves the store as it was. The library writes signals and never commits them.
/// </summary>
public sealed class SignalLibrary(ISignalStore store)
{
    /// <summary>
    ///     Every saved signal in the order of its name; one that does not read stays in the list with its errors.
    /// </summary>
    public IReadOnlyList<SignalEntry> List()
    {
        return [.. store.Names().Order(StringComparer.Ordinal).Select(Entry)];
    }

    public SignalDocument Read(string name)
    {
        if (!SignalNames.IsValid(name) || store.Read(name) is not { } read)
        {
            throw NotFound(name);
        }

        return read.Signal ?? throw new SignalLibraryException(SignalProblem.Unreadable, string.Join("; ", read.Errors));
    }

    /// <summary>
    ///     Writes a new signal; a name that is taken is refused.
    /// </summary>
    public SignalDocument Create(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout)
    {
        SignalDocument signal = Checked(name, description, graph, layout);
        if (store.Exists(name))
        {
            throw Taken(name);
        }

        store.Write(signal);
        return signal;
    }

    /// <summary>
    ///     Replaces a saved signal; it must be there.
    /// </summary>
    public SignalDocument Replace(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout)
    {
        if (!SignalNames.IsValid(name) || !store.Exists(name))
        {
            throw NotFound(name);
        }

        SignalDocument signal = Checked(name, description, graph, layout);
        store.Write(signal);
        return signal;
    }

    public SignalDocument Rename(string name, string to)
    {
        SignalDocument signal = Read(name);
        if (!SignalNames.IsValid(to))
        {
            throw new SignalLibraryException(SignalProblem.Invalid, $"{to} is not a signal name", [NameError(to)]);
        }

        if (to == name)
        {
            return signal;
        }

        if (store.Exists(to))
        {
            throw Taken(to);
        }

        SignalDocument renamed = signal with { Name = to };
        store.Write(renamed);
        store.Delete(name);
        return renamed;
    }

    public void Delete(string name)
    {
        if (!SignalNames.IsValid(name) || !store.Exists(name))
        {
            throw NotFound(name);
        }

        store.Delete(name);
    }

    private SignalEntry Entry(string name)
    {
        try
        {
            SignalDocument signal = Read(name);
            return new SignalEntry(signal.Name, signal.Description, Error: null);
        }
        catch (SignalLibraryException exception)
        {
            return new SignalEntry(name, Description: null, exception.Message);
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
        Dictionary<string, NodePosition> kept = layout.Where(position => ids.Contains(position.Key)).ToDictionary(StringComparer.Ordinal);
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
