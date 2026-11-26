using System.Text.Json;

using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Tests.Integration.Application.Commands.Backtests;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Application.Commands.Runs;

/// <summary>
///     Spec strategy-backtest «Номер версии движка» and «Эталонные стратегии версии движка» (change add-strategy-backtest,
///     task 8.1): each reference strategy, a category of ideas, on the synthetic history of the fake market-data service —
///     four weeks of three pairs, history up to 2026-05-16 — gives the digest of its orders and trades, random entries,
///     weekly means and the numbers of its set that <c>EngineVersions.json</c> records for <see cref="EngineVersion.Number" />.
///     A change of the results of any of them needs a new number and a new entry. The references are the strategies of
///     H0001 and H0002 (<c>Fixtures/H0001.strategy.json</c>, <c>H0002.strategy.json</c>): H0002 holds the position for two
///     days, so every second order comes while it is open. Engine version 5 (change fix-backtest-random-control) changes
///     the random entries and the difference only: the trades of the orders are those of version 4. The fake service pays
///     every pair the same funding, so the orders of the three pairs come at the same moments and take no random entries:
///     the random entries are covered by the tests of the domain.
///     <c>EngineTransition.json</c> keeps what engine version 2 gave the hypotheses H0001 and H0002.
/// </summary>
[NonParallelizable]
public sealed class EngineVersionTests
{
    private const string Overlapping = "H0002";
    private static readonly string[] References = ["H0001", Overlapping];
    private static readonly string Fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    [SetUp]
    public void ShortenHistory()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
    }

    [TearDown]
    public void RestoreHistory()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [Test]
    public async Task The_results_of_the_reference_strategies_are_those_recorded_for_the_engine_version()
    {
        var digests = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string reference in References)
        {
            digests[reference] = EngineDigests.Of(await Run(reference));
        }

        EngineEntry? current = Recorded().SingleOrDefault(entry => entry.Version == EngineVersion.Number);
        string next = $"{{ \"version\": {EngineVersion.Number + 1}, \"digests\": {{ {string.Join(", ", digests.Select(digest => $"\"{digest.Key}\": \"{digest.Value}\""))} }} }}";
        Assert.That(current, Is.Not.Null, $"EngineVersions.json has no entry for engine version {EngineVersion.Number}: {next}");
        Assert.That(
            digests,
            Is.EqualTo(current!.Digests),
            $"The results of a reference strategy changed, or one has no digest: raise EngineVersion.Number and add {next} to EngineVersions.json.");
    }

    /// <summary>
    ///     Spec strategy-backtest «Переход с версии 2» (change fix-backtest-random-control, task 6.2): version 5 changes only
    ///     the random entries and the difference, so the trades of the orders are those recorded for version 4, and they still
    ///     differ from those of version 2 that <c>EngineTransition.json</c> keeps.
    /// </summary>
    [Test]
    public async Task The_trades_of_the_orders_of_engine_version_5_are_those_recorded_for_engine_version_4()
    {
        Dictionary<string, Transition> recorded = JsonSerializer.Deserialize<Dictionary<string, Transition>>(File.ReadAllText(Path.Combine(Fixtures, "EngineTransition.json")), Json)!;
        foreach (string reference in References)
        {
            ReferenceRun run = await Run(reference);

            Assert.Multiple(() =>
            {
                Assert.That(EngineDigests.Orders(run.Rows), Is.EqualTo(Version4Orders[reference]), $"the trades of the orders of {reference}");
                Assert.That(EngineDigests.Orders(run.Rows), Is.Not.EqualTo(recorded[reference].Orders), $"the orders of {reference} moved from those of version 2");
            });
        }
    }

    [Test]
    public void No_two_engine_versions_have_the_same_digests()
    {
        EngineEntry[] recorded = Recorded();

        // Two versions differ when at least one digest differs; a reference only one of them has a digest of is a difference.
        IEnumerable<(int, int)> same =
            from first in recorded
            from second in recorded
            where first.Version < second.Version && Same(first.Digests, second.Digests)
            select (first.Version, second.Version);

        Assert.That(same, Is.Empty, "two engine versions with the same digests: the second changed nothing");
        Assert.That(recorded.Select(entry => entry.Version), Is.Unique);
    }

    [Test]
    public void The_entries_of_versions_1_and_2_stay()
    {
        EngineEntry[] recorded = Recorded();

        Assert.That(EngineVersion.Number, Is.EqualTo(5));
        Assert.That(recorded.Select(entry => entry.Version), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(recorded.Single(entry => entry.Version == 2).Digests["H0001"], Is.EqualTo(recorded.Single(entry => entry.Version == 1).Digests["H0001"]));
        Assert.That(recorded.Single(entry => entry.Version == 1).Digests.Keys, Is.EqualTo(new[] { "H0001" }));
    }

    [Test]
    public async Task The_second_reference_strategy_has_orders_skipped_while_a_position_is_open()
    {
        SetNumbers numbers = (await Run(Overlapping)).Numbers;

        Assert.That(numbers.Skipped, Is.GreaterThan(0), $"{Overlapping} must overlap: without a skipped order the reference does not cover the rule of one position");
    }

    [Test]
    public async Task H0001_has_no_skipped_orders()
    {
        SetNumbers numbers = (await Run("H0001")).Numbers;

        Assert.That(numbers.Skipped, Is.Zero);
        Assert.That(numbers.Trades, Is.GreaterThan(0));
    }

    // The digests of the trades of the orders that engine version 4 recorded (task 27.3 of change add-ideas-and-market), by
    // the name of the reference; version 2 gave those of EngineTransition.json.
    private static readonly Dictionary<string, string> Version4Orders = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["H0001"] = "d3d55c81204f25f0c570e55353c71692343b38ffb015e196975a2a3fd265e81d",
        ["H0002"] = "9cacfe641a799a07d29404053d92708e14f62b3a90bb9e2e77d766307a9dc3b3"
    };

    private static async Task<ReferenceRun> Run(string reference)
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        return await ReferenceBacktests.Run(server, reference);
    }

    private static bool Same(Dictionary<string, string> first, Dictionary<string, string> second)
    {
        return first.Count == second.Count && first.All(digest => second.TryGetValue(digest.Key, out string? other) && other == digest.Value);
    }

    private static EngineEntry[] Recorded()
    {
        return JsonSerializer.Deserialize<EngineEntry[]>(File.ReadAllText(Path.Combine(Fixtures, "EngineVersions.json")), Json)!;
    }

    // The digests of an engine version by the name of the reference.
    private sealed record EngineEntry(int Version, Dictionary<string, string> Digests);

    // What engine version 2 gave a hypothesis: the hash of its text, the digest of the trades of its orders and that of its
    // random entries.
    private sealed record Transition(string Seed, string Orders, string Randoms);
}