using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
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
    private static readonly DateTimeOffset End = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);
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

    [Test]
    public async Task With_the_hash_of_the_hypothesis_as_the_seed_the_strategies_give_the_trades_of_engine_version_2()
    {
        Dictionary<string, Transition> recorded = JsonSerializer.Deserialize<Dictionary<string, Transition>>(File.ReadAllText(Path.Combine(Fixtures, "EngineTransition.json")), Json)!;
        foreach (string reference in References)
        {
            Transition version2 = recorded[reference];

            (IReadOnlyList<EventRow> rows, _) = await Run(reference, version2.Seed);
            (IReadOnlyList<EventRow> own, _) = await Run(reference, seed: null);

            Assert.That(EngineDigests.Orders(rows), Is.EqualTo(version2.Orders), $"the trades of the orders of {reference}");
            Assert.That(EngineDigests.Randoms(rows), Is.EqualTo(version2.Randoms), $"the random entries of {reference} and their trades");
            Assert.That(EngineDigests.Orders(own), Is.EqualTo(version2.Orders), "the orders do not depend on the seed");
            Assert.That(EngineDigests.Randoms(own), Is.Not.EqualTo(version2.Randoms), "the seed of the graph draws other random entries");
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

        Assert.That(EngineVersion.Number, Is.EqualTo(3));
        Assert.That(recorded.Select(entry => entry.Version), Is.EqualTo(new[] { 1, 2, 3 }));
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

    // The strategy of the reference as a backtest of one set on 4h over every pair, the pairs in the order of the catalog;
    // with a seed, the draw of random entries takes it instead of that of the graph.
    private static async Task<(IReadOnlyList<EventRow> Rows, SetNumbers Numbers)> Run(string reference, string? seed)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, $"{reference}.strategy.json")));
        GraphDefinition graph = StrategyGraph.ReadForm(document.RootElement).Value!;
        var settings = new BacktestSettings([], [TimeSpan.FromHours(4)], HistoryPeriod.DataStart, End, Pairs: null, 0.00055, 0.0002);
        BacktestPlan plan = BacktestPlans.Of(graph, StrategyKind.Idea, settings, BacktestGrid.Of(settings)).Single();
        if (seed is not null)
        {
            plan = plan with { Design = plan.Design with { Seed = seed } };
        }

        await using MarketDataServer server = await MarketDataServer.Start();
        await using ServiceProvider services = BenchServices.Start(server);
        using var hash = new HistoryHash();
        var history = new MarketHistory(services.GetRequiredService<IHistorySource>(), End, hash);
        List<EventRow> rows = [];
        SetNumbers numbers = SetNumbers.Zero;
        foreach (Instrument pair in await services.GetRequiredService<IInstrumentCatalog>().LinearUsdtPerpetuals(CancellationToken.None))
        {
            PairSetResult result = (await PairBacktestRun.RunAsync(history, pair, StrategyKind.Idea, [plan], CancellationToken.None)).Single();
            rows.AddRange(result.Rows);
            numbers += result.Numbers;
        }

        return (rows, numbers);
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
