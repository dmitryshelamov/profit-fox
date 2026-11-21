using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitFox.Application.Commands.Hypotheses;
using ProfitFox.Application.Commands.Runs;
using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Verdicts;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Runs;

/// <summary>
///     Spec hypothesis-bench «Номер версии движка» and «Эталонные гипотезы версии движка»: each reference hypothesis on the
///     synthetic history of the fake market-data service gives the same digest of events, outcomes, block means and verdict
///     numbers as the file <c>EngineVersions.json</c> records for <see cref="EngineVersion.Number" />. A change of the
///     results of any of them needs a new number and a new entry. The reference hypotheses are H0001 and H0002, which is
///     H0001 with a short for two days: its events come once a day, so every second one comes while the position is open
///     (change add-strategy-graph, design.md, decision 9).
/// </summary>
[NonParallelizable]
public sealed class EngineVersionTests
{
    private const string Overlapping = "H0002";
    private static readonly string[] References = ["H0001", Overlapping];
    private static readonly DateTimeOffset Today = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
    private readonly HypothesisCatalog _hypotheses = new HypothesisCatalog(new HypothesisStore(BenchDatabase.DataSource));

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        foreach (string reference in References)
        {
            await _hypotheses.Add(File.ReadAllText(Path.Combine(Fixtures, $"{reference}.json")), CancellationToken.None);
        }
    }

    [TearDown]
    public void RemoveBench()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task The_results_of_the_reference_hypotheses_are_those_recorded_for_the_engine_version()
    {
        var digests = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string reference in References)
        {
            digests[reference] = Digest(await Run(reference));
        }

        EngineEntry? current = Recorded().SingleOrDefault(entry => entry.Version == EngineVersion.Number);
        string next = $"{{ \"version\": {EngineVersion.Number + 1}, \"digests\": {{ {string.Join(", ", digests.Select(digest => $"\"{digest.Key}\": \"{digest.Value}\""))} }} }}";
        Assert.That(current, Is.Not.Null, $"EngineVersions.json has no entry for engine version {EngineVersion.Number}");
        Assert.That(
            digests,
            Is.EqualTo(current!.Digests),
            $"The results of a reference hypothesis changed, or one has no digest: raise EngineVersion.Number and add {next} to EngineVersions.json.");
    }

    [Test]
    public void No_two_engine_versions_have_the_same_digests()
    {
        EngineEntry[] recorded = Recorded();

        // Two versions differ when at least one digest differs; a hypothesis only one of them has a digest of is a difference.
        IEnumerable<(int, int)> same =
            from first in recorded
            from second in recorded
            where first.Version < second.Version && Same(first.Digests, second.Digests)
            select (first.Version, second.Version);

        Assert.That(same, Is.Empty, "two engine versions with the same digests: the second changed nothing");
        Assert.That(recorded.Select(entry => entry.Version), Is.Unique);
    }

    [Test]
    public void H0001_gives_under_engine_version_2_the_results_of_version_1()
    {
        EngineEntry[] recorded = Recorded();

        // Version 2 skips an event that comes while a position is open; the events of H0001 do not overlap.
        Assert.That(EngineVersion.Number, Is.GreaterThanOrEqualTo(2));
        Assert.That(recorded.Single(entry => entry.Version == 2).Digests["H0001"], Is.EqualTo(recorded.Single(entry => entry.Version == 1).Digests["H0001"]));
        Assert.That(recorded.Single(entry => entry.Version == 1).Digests.Keys, Is.EqualTo(new[] { "H0001" }), "version 1 of the second reference was never computed");
    }

    [Test]
    public async Task The_second_reference_hypothesis_has_events_skipped_while_a_position_is_open()
    {
        RunResult result = await Run(Overlapping);

        Assert.That(result.Skipped, Is.GreaterThan(0), $"{Overlapping} must overlap: without a skipped event the reference does not cover the rule of one position");
        Assert.That(result.Report, Does.Contain($"| Событий пропущено при открытой позиции | {result.Skipped} |"));
    }

    [Test]
    public async Task The_report_of_H0001_shows_no_skipped_events()
    {
        RunResult result = await Run("H0001");

        Assert.That(result.Skipped, Is.Zero);
        Assert.That(result.Report, Does.Contain("| Событий пропущено при открытой позиции | 0 |"));
    }

    [Test]
    public async Task The_ledger_line_carries_the_engine_version()
    {
        RunResult result = await Run("H0001");

        Assert.That(result.Entry.EngineVersion, Is.EqualTo(EngineVersion.Number));
        Assert.That(result.Entry.Version, Is.EqualTo(1));
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        string report = await connection.ExecuteScalarAsync<string>("SELECT text FROM reports WHERE run_id = @RunId", new { result.Entry.RunId }) ?? "";
        Assert.That(report, Does.Contain($"| Версия движка | {EngineVersion.Number} |").And.Contain("| Версия гипотезы | 1 |"));
    }

    private async Task<RunResult> Run(string hypothesis)
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var runner = new HypothesisRunner(
            _hypotheses,
            new GrpcHistorySource(server.Channel),
            new InstrumentCatalog(server.Http),
            new RunLedger(BenchDatabase.DataSource),
            TextWriter.Null);
        return await runner.Run(new RunOptions(hypothesis, Version: null, Period.Exploration, Parity: false, Today), CancellationToken.None);
    }

    private static bool Same(Dictionary<string, string> first, Dictionary<string, string> second)
    {
        return first.Count == second.Count && first.All(digest => second.TryGetValue(digest.Key, out string? other) && other == digest.Value);
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

    // The digests of an engine version by the id of the reference hypothesis.
    private sealed record EngineEntry(int Version, Dictionary<string, string> Digests);
}
