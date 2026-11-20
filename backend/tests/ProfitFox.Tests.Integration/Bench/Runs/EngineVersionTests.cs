using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Application.Runs;
using ProfitFox.Bench.Domain.Runs;
using ProfitFox.Bench.Domain.Verdicts;
using ProfitFox.Bench.Infrastructure.Hypotheses;
using ProfitFox.Bench.Infrastructure.Ledger;
using ProfitFox.Bench.Infrastructure.MarketData;
using ProfitFox.Engine.Simulation.Trades;
using ProfitFox.Tests.Integration.Bench.Database;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Runs;

/// <summary>
///     Spec hypothesis-bench «Номер версии движка»: H0001 on the synthetic history of the fake market-data service gives
///     the same digest of events, outcomes, block means and verdict numbers as the file <c>EngineVersions.json</c> records
///     for <see cref="EngineVersion.Number" />. A change of results needs a new number and a new entry.
/// </summary>
[NonParallelizable]
public sealed class EngineVersionTests
{
    private static readonly DateTimeOffset Today = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
    private readonly HypothesisCatalog _hypotheses = new HypothesisCatalog(new HypothesisStore(BenchDatabase.DataSource));

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        await _hypotheses.Add(File.ReadAllText(Path.Combine(Fixtures, "H0001.json")), CancellationToken.None);
    }

    [TearDown]
    public void RemoveBench()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task The_results_of_H0001_are_those_recorded_for_the_engine_version()
    {
        RunResult result = await Run();

        string digest = Digest(result);

        EngineEntry[] recorded = Recorded();
        EngineEntry? current = recorded.SingleOrDefault(entry => entry.Version == EngineVersion.Number);
        Assert.That(current, Is.Not.Null, $"EngineVersions.json has no entry for engine version {EngineVersion.Number}");
        Assert.That(digest, Is.EqualTo(current!.Digest),
            $"The results of H0001 changed: raise EngineVersion.Number and add {{ \"version\": {EngineVersion.Number + 1}, \"digest\": \"{digest}\" }} to EngineVersions.json.");
    }

    [Test]
    public void No_two_engine_versions_have_one_digest()
    {
        IEnumerable<string> repeated = Recorded().GroupBy(entry => entry.Digest).Where(group => group.Count() > 1).Select(group => group.Key);

        Assert.That(repeated, Is.Empty, "two engine versions with the same digest: the second changed nothing");
    }

    [Test]
    public async Task The_ledger_line_carries_the_engine_version()
    {
        RunResult result = await Run();

        Assert.That(result.Entry.EngineVersion, Is.EqualTo(EngineVersion.Number));
        Assert.That(result.Entry.Version, Is.EqualTo(1));
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        string report = await connection.ExecuteScalarAsync<string>("SELECT text FROM reports WHERE run_id = @RunId", new { result.Entry.RunId }) ?? "";
        Assert.That(report, Does.Contain($"| Версия движка | {EngineVersion.Number} |").And.Contain("| Версия гипотезы | 1 |"));
    }

    private async Task<RunResult> Run()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var runner = new HypothesisRunner(
            _hypotheses,
            new GrpcHistorySource(server.Channel),
            new InstrumentCatalog(server.Http),
            new RunLedger(BenchDatabase.DataSource),
            TextWriter.Null);
        return await runner.Run(new RunOptions("H0001", Version: null, Period.Exploration, Parity: false, Today), CancellationToken.None);
    }

    private static EngineEntry[] Recorded()
    {
        return JsonSerializer.Deserialize<EngineEntry[]>(
            File.ReadAllText(Path.Combine(Fixtures, "EngineVersions.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static string Digest(RunResult result)
    {
        var text = new StringBuilder();
        foreach (EventRow row in result.Rows)
        {
            Line(text, "event", row.Instrument, row.Moment.ToUnixTimeMilliseconds(), row.Block, row.Status, row.RandomValid, row.PseudoDifference);
            Fields(text, "event-fields", row.Event);
            Fields(text, "random-mean", row.RandomMean);
            Outcome(text, "event-outcome", row.Outcome);
            foreach (RandomTrade random in row.Randoms)
            {
                Line(text, "random", random.Entry.ToUnixTimeMilliseconds());
                Outcome(text, "random-outcome", random.Outcome);
            }
        }

        foreach ((long block, double mean, int events) in Evidence.PseudoBlockMeans(result.Rows))
        {
            Line(text, "pseudo-block", block, mean, events);
        }

        foreach ((long block, double mean, int events) in Evidence.BlockMeans(result.Rows))
        {
            Line(text, "block", block, mean, events);
        }

        Verdict verdict = result.Verdict!;
        Line(text, "verdict", verdict.Kind, verdict.Attempts, verdict.RequiredT, verdict.Blocks, verdict.Events, verdict.Power, verdict.PseudoSd,
            verdict.T, verdict.Mean, verdict.FirstHalfMean, verdict.SecondHalfMean);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void Fields(StringBuilder text, string name, TradeFields? fields)
    {
        Line(text, name, fields?.Y, fields?.Gross, fields?.Fees, fields?.Slippage, fields?.Funding, fields?.Net);
    }

    private static void Outcome(StringBuilder text, string name, TradeOutcome? outcome)
    {
        Line(text, name, outcome?.Status, outcome?.ExitReason, outcome?.Ambiguous, outcome?.EntryTime?.ToUnixTimeMilliseconds(),
            outcome?.ExitTime?.ToUnixTimeMilliseconds(), outcome?.EntryPrice, outcome?.ExitPrice, outcome?.Gross, outcome?.Fees,
            outcome?.Slippage, outcome?.Funding, outcome?.Net, outcome?.R);
    }

    // Numbers are cut to 12 significant digits so that the last bit of a platform's math does not change the digest.
    private static void Line(StringBuilder text, params object?[] values)
    {
        text.AppendJoin(';', values.Select(value => value switch
        {
            null => "-",
            double number => number.ToString("G12", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        }));
        text.Append('\n');
    }

    private sealed record EngineEntry(int Version, string Digest);
}
