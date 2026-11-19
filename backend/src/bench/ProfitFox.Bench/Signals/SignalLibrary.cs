using System.Text.Json;
using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Bench.Signals;

/// <summary>
///     The saved signals of the constructor (spec signal-constructor): one file <c>&lt;name&gt;.json</c> each in
///     <paramref name="folder" />, usually <c>bench/signals/</c>. A graph is written only when it reads as a signal, and
///     through a temporary file, so a broken write never leaves half a JSON. The library writes files and never commits
///     them.
/// </summary>
public sealed class SignalLibrary(string folder)
{
    private const string Extension = ".json";

    public string Folder { get; } = folder;

    /// <summary>
    ///     Every signal file in the order of its name; a file that does not read stays in the list with its errors.
    /// </summary>
    public IReadOnlyList<SignalEntry> List()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        return [.. Directory.GetFiles(Folder, "*" + Extension)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !name.StartsWith('.'))
            .Order(StringComparer.Ordinal)
            .Select(Entry)];
    }

    public SignalDocument Read(string name)
    {
        string path = PathOf(name);
        if (!File.Exists(path))
        {
            throw NotFound(name);
        }

        (SignalDocument? signal, IReadOnlyList<string> errors) = SignalJson.Read(File.ReadAllText(path), name);
        return signal ?? throw new SignalLibraryException(SignalProblem.Unreadable, string.Join("; ", errors));
    }

    /// <summary>
    ///     Writes a new signal; a name that is taken is refused.
    /// </summary>
    public SignalDocument Create(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout)
    {
        SignalDocument signal = Checked(name, description, graph, layout);
        if (File.Exists(PathOf(name)))
        {
            throw Taken(name);
        }

        Write(signal);
        return signal;
    }

    /// <summary>
    ///     Replaces a saved signal; the file must be there.
    /// </summary>
    public SignalDocument Replace(string name, string? description, JsonElement graph, IReadOnlyDictionary<string, NodePosition> layout)
    {
        if (!SignalNames.IsValid(name) || !File.Exists(PathOf(name)))
        {
            throw NotFound(name);
        }

        SignalDocument signal = Checked(name, description, graph, layout);
        Write(signal);
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

        if (File.Exists(PathOf(to)))
        {
            throw Taken(to);
        }

        SignalDocument renamed = signal with { Name = to };
        Write(renamed);
        File.Delete(PathOf(name));
        return renamed;
    }

    public void Delete(string name)
    {
        string path = PathOf(name);
        if (!SignalNames.IsValid(name) || !File.Exists(path))
        {
            throw NotFound(name);
        }

        File.Delete(path);
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

    private void Write(SignalDocument signal)
    {
        Directory.CreateDirectory(Folder);
        string path = PathOf(signal.Name);
        string temporary = Path.Combine(Folder, $".{signal.Name}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, SignalJson.Write(signal));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // A name is checked before it becomes a path, so «../» never leaves the folder.
    private string PathOf(string name)
    {
        return SignalNames.IsValid(name) ? Path.Combine(Folder, name + Extension) : Path.Combine(Folder, ".invalid-name");
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
