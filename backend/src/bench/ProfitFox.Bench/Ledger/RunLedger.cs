using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfitFox.Bench.Ledger;

/// <summary>
///     The ledger of runs, <c>bench/ledger.jsonl</c> (spec hypothesis-bench): a JSON line per run, only ever appended. It
///     keeps a hypothesis id to one hash, makes a verdict of a period final, lets the holdout open once and only after the
///     hypothesis passed exploration, and counts the attempts the required t grows with.
/// </summary>
public sealed class RunLedger(string path)
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Path { get; } = path;

    public IReadOnlyList<LedgerEntry> Entries()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        return [.. File.ReadLines(Path).Where(line => line.Trim().Length > 0).Select(line => JsonSerializer.Deserialize<LedgerEntry>(line, Json)!)];
    }

    public void Append(LedgerEntry entry)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        using var file = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(file);
        writer.Write(JsonSerializer.Serialize(entry, Json));
        writer.Write('\n');
    }

    /// <summary>
    ///     Refuses a run of <paramref name="hypothesis" /> with <paramref name="hash" /> on <paramref name="period" /> that the
    ///     rules of the ledger do not allow. A parity run is blind and only needs the hash to match.
    /// </summary>
    public void CheckCanRun(string hypothesis, string hash, string period, bool parity)
    {
        IReadOnlyList<LedgerEntry> entries = [.. Entries().Where(entry => entry.Hypothesis == hypothesis && !IsCorrected(entry))];
        LedgerEntry? other = entries.FirstOrDefault(entry => entry.Hash != hash);
        if (other is not null)
        {
            throw new LedgerException(
                $"{hypothesis} was run with another file (hash {other.Hash[..12]}…, run {other.RunId}); a changed hypothesis is a new hypothesis with a new id.");
        }

        if (parity)
        {
            return;
        }

        LedgerEntry? final = entries.FirstOrDefault(entry => entry.Period == period && RunStatus.IsFinal(entry.Status));
        if (final is not null)
        {
            throw new LedgerException($"{hypothesis} already has the verdict {final.Status} on {period} (run {final.RunId}).");
        }

        if (period == "holdout" && !entries.Any(entry => entry.Period == "exploration" && entry.Status == RunStatus.Passed))
        {
            throw new LedgerException($"The holdout opens only after {hypothesis} passed exploration.");
        }
    }

    /// <summary>
    ///     The attempts of a period: the different hypotheses with a final verdict in it, this one included.
    /// </summary>
    public int Attempts(string hypothesis, string period)
    {
        HashSet<string> tried = [.. Entries()
            .Where(entry => entry.Period == period && RunStatus.IsFinal(entry.Status) && !IsCorrected(entry))
            .Select(entry => entry.Hypothesis)];
        tried.Add(hypothesis);
        return tried.Count;
    }

    private bool IsCorrected(LedgerEntry entry)
    {
        return Entries().Any(other => other.Corrects == entry.RunId);
    }
}
