using System.Diagnostics;

namespace ProfitFox.Bench.Ledger;

/// <summary>
///     The git repository at <paramref name="root" />.
/// </summary>
public sealed class GitRepository(string root) : IRepository
{
    public IReadOnlyList<string> Uncommitted()
    {
        return Changed(Git("status", "--porcelain", "--untracked-files=all"));
    }

    public string Commit()
    {
        return Git("rev-parse", "HEAD").Trim();
    }

    /// <summary>
    ///     The changed paths of <c>git status --porcelain</c>, without the ledger and the reports the bench writes itself.
    /// </summary>
    public static IReadOnlyList<string> Changed(string porcelain)
    {
        return [.. porcelain.Split('\n')
            .Where(line => line.Length > 3)
            .Select(line => line[3..].Trim().Trim('"'))
            .Select(path => path.Contains(" -> ", StringComparison.Ordinal) ? path[(path.IndexOf(" -> ", StringComparison.Ordinal) + 4)..] : path)
            .Where(path => path != "bench/ledger.jsonl" && !path.StartsWith("bench/reports/", StringComparison.Ordinal))];
    }

    private string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error.Trim()}");
        }

        return output;
    }
}
