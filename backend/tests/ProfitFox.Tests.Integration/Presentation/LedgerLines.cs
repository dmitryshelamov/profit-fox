using Dapper;

using Npgsql;

using ProfitFox.Application.Runs;
using ProfitFox.Domain.Ledger;
using ProfitFox.Infrastructure.Ledger;

namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     Writes lines of the ledger, each with the finished row of its run, the way the bench does, for tests that need a verdict that is already there.
/// </summary>
internal static class LedgerLines
{
    /// <summary>
    ///     A line of <paramref name="hypothesis" /> version <paramref name="version" /> with <paramref name="status" /> on
    ///     <paramref name="period" />.
    /// </summary>
    public static async Task<LedgerEntry> Write(string hypothesis, int version, string hash, string period, string status)
    {
        var entry = new LedgerEntry
        {
            RunId = Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = hypothesis,
            Hash = hash,
            Version = version,
            EngineVersion = 1,
            Period = period,
            Status = status,
            Reason = "test line"
        };
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO runs (id, hypothesis_id, version, period, kind, status, finished_at) VALUES (@RunId, @Hypothesis, @Version, @Period, @Kind, 'finished', now())",
            new { entry.RunId, entry.Hypothesis, entry.Version, entry.Period, Kind = status == RunStatus.Parity ? "parity" : "verdict" });
        await new RunLedger(BenchDatabase.DataSource).Decide(_ => new Decision(entry, Report: "# report"), CancellationToken.None);
        return entry;
    }
}
