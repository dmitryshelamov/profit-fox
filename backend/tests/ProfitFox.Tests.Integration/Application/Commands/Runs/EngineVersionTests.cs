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
///     days, so every second order comes while it is open. <c>EngineTransition.json</c> keeps what engine version 2 gave
///     the hypotheses H0001 and H0002: with the hash of the hypothesis as the seed the strategies give the same trades of
///     orders and of random entries.
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
            (IReadOnlyList<EventRow> rows, SetNumbers numbers) = await Run(reference, seed: null);
            digests[reference] = EngineDigests.Of(rows, numbers);
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
    ///     With the hash of the hypothesis as the seed the strategies gave the trades of engine version 2 up to version 3.
    ///     Version 4 (change add-ideas-and-market, task 27.3) puts a payment at the close of a candle on the candle after it:
    ///     the orders of H0001 and H0002 come later than in version 2, and the random entries drawn for them go with them. So
    ///     the same seed now gives the trades recorded here for version 4; <c>EngineTransition.json</c> keeps what version 2
    ///     gave, and the orders still do not depend on the seed.
    /// </summary>
    [Test]
    public async Task With_the_hash_of_the_hypothesis_as_the_seed_the_strategies_give_the_trades_recorded_for_engine_version_4()
    {
        Dictionary<string, Transition> recorded = JsonSerializer.Deserialize<Dictionary<string, Transition>>(File.ReadAllText(Path.Combine(Fixtures, "EngineTransition.json")), Json)!;
        foreach (string reference in References)
        {
            Transition version2 = recorded[reference];
            (string orders, string randoms) = Version4[reference];

            (IReadOnlyList<EventRow> rows, _) = await Run(reference, version2.Seed);
            (IReadOnlyList<EventRow> own, _) = await Run(reference, seed: null);

            Assert.Multiple(() =>
            {
                Assert.That(EngineDigests.Orders(rows), Is.EqualTo(orders), $"the trades of the orders of {reference}");
                Assert.That(EngineDigests.Randoms(rows), Is.EqualTo(randoms), $"the random entries of {reference} and their trades");
                Assert.That(EngineDigests.Orders(rows), Is.Not.EqualTo(version2.Orders), $"the orders of {reference} moved from those of version 2");
                Assert.That(EngineDigests.Orders(own), Is.EqualTo(orders), "the orders do not depend on the seed");
                Assert.That(EngineDigests.Randoms(own), Is.Not.EqualTo(randoms), "the seed of the graph draws other random entries");
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

        Assert.That(EngineVersion.Number, Is.EqualTo(4));
        Assert.That(recorded.Select(entry => entry.Version), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(recorded.Single(entry => entry.Version == 2).Digests["H0001"], Is.EqualTo(recorded.Single(entry => entry.Version == 1).Digests["H0001"]));
        Assert.That(recorded.Single(entry => entry.Version == 1).Digests.Keys, Is.EqualTo(new[] { "H0001" }));
    }

    [Test]
    public async Task The_second_reference_strategy_has_orders_skipped_while_a_position_is_open()
    {
        (_, SetNumbers numbers) = await Run(Overlapping, seed: null);

        Assert.That(numbers.Skipped, Is.GreaterThan(0), $"{Overlapping} must overlap: without a skipped order the reference does not cover the rule of one position");
    }

    [Test]
    public async Task H0001_has_no_skipped_orders()
    {
        (_, SetNumbers numbers) = await Run("H0001", seed: null);

        Assert.That(numbers.Skipped, Is.Zero);
        Assert.That(numbers.Trades, Is.GreaterThan(0));
    }

    // What the hash of the hypothesis as the seed gives in engine version 4 (task 27.3), by the name of the reference: the
    // digests of the trades of the orders and of the random entries. Version 2 gave those of EngineTransition.json.
    private static readonly Dictionary<string, (string Orders, string Randoms)> Version4 = new Dictionary<string, (string Orders, string Randoms)>(StringComparer.Ordinal)
    {
        ["H0001"] = ("d3d55c81204f25f0c570e55353c71692343b38ffb015e196975a2a3fd265e81d", "b1df364ef629dfcccce8d8d60a4c5c2c84c58ea01b9268b286a4fe206a978250"),
        ["H0002"] = ("9cacfe641a799a07d29404053d92708e14f62b3a90bb9e2e77d766307a9dc3b3", "503d2c9ea6dde2c4aa04f32c6cbbef9d9d8ccbe6212c856d753cb1942da0a14b")
    };

    private static async Task<(IReadOnlyList<EventRow> Rows, SetNumbers Numbers)> Run(string reference, string? seed)
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        return await ReferenceBacktests.Run(server, reference, seed);
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

    // What engine version 2 gave a hypothesis: the hash of its text, the digest of the trades of its orders and that of its random entries.
    private sealed record Transition(string Seed, string Orders, string Randoms);
}
