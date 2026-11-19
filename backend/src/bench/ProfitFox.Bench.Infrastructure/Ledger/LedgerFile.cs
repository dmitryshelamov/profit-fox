using System.Text.Json;
using System.Text.Json.Serialization;
using ProfitFox.Bench.Application.Runs;
using ProfitFox.Bench.Domain.Ledger;

namespace ProfitFox.Bench.Infrastructure.Ledger;

/// <summary>
///     The ledger of runs as the file <c>bench/ledger.jsonl</c> (spec hypothesis-bench): a JSON line per run, only ever
///     appended.
/// </summary>
public sealed class LedgerFile(string path) : IRunLedger
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
}
