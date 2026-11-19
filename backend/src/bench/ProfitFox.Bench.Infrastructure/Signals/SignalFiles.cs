using ProfitFox.Bench.Application.Signals;
using ProfitFox.Bench.Domain.Signals;

namespace ProfitFox.Bench.Infrastructure.Signals;

/// <summary>
///     The saved signals as files (spec signal-constructor): one <c>&lt;name&gt;.json</c> each in
///     <paramref name="folder" />, usually <c>bench/signals/</c>. A signal is written through a temporary file, so a broken
///     write never leaves half a JSON; files whose name starts with a dot are not signals.
/// </summary>
public sealed class SignalFiles(string folder) : ISignalStore
{
    private const string Extension = ".json";

    public string Folder { get; } = folder;

    public IReadOnlyList<string> Names()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        return [.. Directory.GetFiles(Folder, "*" + Extension)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !name.StartsWith('.'))];
    }

    public bool Exists(string name)
    {
        return File.Exists(PathOf(name));
    }

    public SignalRead? Read(string name)
    {
        string path = PathOf(name);
        if (!File.Exists(path))
        {
            return null;
        }

        (SignalDocument? signal, IReadOnlyList<string> errors) = SignalJson.Read(File.ReadAllText(path), name);
        return new SignalRead(signal, errors);
    }

    public void Write(SignalDocument signal)
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

    public void Delete(string name)
    {
        File.Delete(PathOf(name));
    }

    // A name is checked before it becomes a path, so «../» never leaves the folder.
    private string PathOf(string name)
    {
        return SignalNames.IsValid(name) ? Path.Combine(Folder, name + Extension) : Path.Combine(Folder, ".invalid-name");
    }
}
