using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Чтение идей", "Своя идея" and "Сетапы пары в снимке", decision 8.</summary>
public class IdeasApiTests
{
    // The clock of the app reads 2024-03-10T12:30Z.
    private static readonly DateTimeOffset SnapshotAt = DateTimeOffset.Parse("2024-03-10T12:29:10Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Eth = new Instrument(MarketCategory.Spot, "ETHUSDT");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private NpgsqlDataSource _database = null!;
    private Guid _holder;

    [SetUp]
    public async Task SetUp()
    {
        _factory = ProfitFoxFactory.Create(workers: 0);
        _client = _factory.CreateClient();
        _database = NpgsqlDataSource.Create(_factory.ConnectionString);
        _holder = Guid.NewGuid();
        await new ScannerStateRepository(_database).ClaimAsync(_holder, SnapshotAt, SnapshotAt.AddSeconds(30), CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _database.DisposeAsync();
        await _factory.DisposeAsync();
    }

    // Scenario "Только long": each idea has the probability of its bucket, numbers as decimal strings.
    [Test]
    public async Task Ideas_of_one_side_come_with_the_statistics_of_their_bucket()
    {
        Idea impulse = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), IdeaStatus.Active, Sol, tick: 0.01m);
        Idea drop = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-10), instrument: Sol, setup: IdeaSetup.Drop, side: IdeaSide.Short);
        Idea closedLongAgo = IdeaGenerator.Generate(SnapshotAt.AddDays(-3), IdeaStatus.Stop, Sol, setup: IdeaSetup.EarlyImpulse, statusAt: SnapshotAt.AddDays(-2));
        await new IdeaRepository(_database).SaveAsync([impulse, drop, closedLongAgo], [], CancellationToken.None);
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        await new IdeaBucketRepository(_database).ReplaceAsync(
            IdeasEngineVersion.Current,
            [
                new IdeaBucket(key, 90, BucketStatisticsCalculator.Compute(180, 200, 20, 40m, 400m)),
                new IdeaBucket(key, 30, BucketStatisticsCalculator.Compute(6, 5, 1, 2.4m, 20m))
            ],
            null, CancellationToken.None);

        JsonElement ideas = await ItemsAsync("/api/ideas?side=long");

        JsonElement idea = ideas.EnumerateArray().Single();
        idea.GetProperty("id").GetGuid().ShouldBe(impulse.Id);
        (idea.GetProperty("category").GetString(), idea.GetProperty("symbol").GetString(), idea.GetProperty("setup").GetString()).ShouldBe(("linear", "SOLUSDT", "impulse"));
        (idea.GetProperty("interval").GetString(), idea.GetProperty("type").GetString(), idea.GetProperty("side").GetString()).ShouldBe(("15", "target_2r", "long"));
        (idea.GetProperty("entry").GetString(), idea.GetProperty("stop").GetString(), idea.GetProperty("target").GetString(), idea.GetProperty("riskReward").GetString())
            .ShouldBe(("105", "100", "115", "2"));
        (idea.GetProperty("status").GetString(), idea.GetProperty("move").GetString(), idea.GetProperty("volumeRatio").GetString()).ShouldBe(("active", "3.5", "2.25"));
        idea.GetProperty("tick").GetString().ShouldBe("0.01");
        ShouldHaveMoney(idea);
        // Scenario "Корзина в ответе": the share of targets among all 400 cases; the mean +0.1 R, s ≈ 0.996 R, so the interval is above zero.
        JsonElement bucket = idea.GetProperty("bucket");
        (bucket.GetProperty("cases").GetInt32(), bucket.GetProperty("targets").GetInt32(), bucket.GetProperty("stops").GetInt32(), bucket.GetProperty("expired").GetInt32())
            .ShouldBe((400, 180, 200, 20));
        (bucket.GetProperty("probability").GetString(), bucket.GetProperty("enoughData").GetBoolean()).ShouldBe(("0.45", true));
        decimal.Parse(bucket.GetProperty("probabilityLow").GetString()!, CultureInfo.InvariantCulture).ShouldBe(0.4019m, 0.0001m);
        decimal.Parse(bucket.GetProperty("probabilityHigh").GetString()!, CultureInfo.InvariantCulture).ShouldBe(0.4990m, 0.0001m);
        bucket.GetProperty("expectancy").GetString().ShouldBe("0.1");
        decimal.Parse(bucket.GetProperty("expectancyLow").GetString()!, CultureInfo.InvariantCulture).ShouldBe(0.0024m, 0.0001m);
        decimal.Parse(bucket.GetProperty("expectancyHigh").GetString()!, CultureInfo.InvariantCulture).ShouldBe(0.1976m, 0.0001m);
        bucket.GetProperty("badge").GetString().ShouldBe("up");
        JsonElement recent = idea.GetProperty("recent");
        (recent.GetProperty("cases").GetInt32(), recent.GetProperty("enoughData").GetBoolean(), recent.GetProperty("probability").ValueKind).ShouldBe((12, false, JsonValueKind.Null));
        (recent.GetProperty("expectancyLow").ValueKind, recent.GetProperty("badge").ValueKind).ShouldBe((JsonValueKind.Null, JsonValueKind.Null));
    }

    // Scenario "Корзина модели в ответе" and decision 7 of honest-idea-intervals: the numbers of the model with the own ones beside; a bucket of
    // few own cases has enough data by its parent. The list goes by the expectancy of the model: −0.10 R of the impulse before −0.20 R of the
    // drop, whose own +0.50 R would come first.
    [Test]
    public async Task Ideas_come_with_the_numbers_of_the_model_of_their_bucket_and_go_by_them()
    {
        Idea impulse = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Sol);
        Idea drop = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-10), instrument: Sol, setup: IdeaSetup.Drop);
        await new IdeaRepository(_database).SaveAsync([impulse, drop], [], CancellationToken.None);
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        await new IdeaBucketRepository(_database).ReplaceAsync(
            IdeasEngineVersion.Current,
            [
                new IdeaBucket(key, 90, new BucketStatistics(35, 14, 21, 0, 0.36m, 0.25m, 0.47m, -0.10m, -0.279m, 0.075m)
                {
                    OwnProbability = 0.4m,
                    OwnProbabilityLow = 0.25m,
                    OwnProbabilityHigh = 0.56m,
                    OwnExpectancy = 0.30m,
                    OwnExpectancyLow = 0.02m,
                    OwnExpectancyHigh = 0.58m,
                    ParentWeight = 0.80m
                }),
                new IdeaBucket(key with { Setup = IdeaSetup.Drop }, 90, new BucketStatistics(12, 6, 6, 0, 0.35m, 0.3m, 0.4m, -0.20m, -0.3m, -0.1m)
                {
                    OwnExpectancy = 0.50m,
                    ParentWeight = 0.92m,
                    FewOwnCases = true
                })
            ],
            null, CancellationToken.None);

        JsonElement[] ideas = [.. (await ItemsAsync("/api/ideas")).EnumerateArray()];

        ideas.Select(idea => idea.GetProperty("id").GetGuid()).ShouldBe([impulse.Id, drop.Id]);
        JsonElement bucket = ideas[0].GetProperty("bucket");
        (bucket.GetProperty("expectancy").GetString(), bucket.GetProperty("ownExpectancy").GetString(), bucket.GetProperty("parentWeight").GetString()).ShouldBe(("-0.10", "0.30", "0.80"));
        (bucket.GetProperty("ownProbability").GetString(), bucket.GetProperty("ownProbabilityLow").GetString(), bucket.GetProperty("ownProbabilityHigh").GetString())
            .ShouldBe(("0.4", "0.25", "0.56"));
        (bucket.GetProperty("ownExpectancyLow").GetString(), bucket.GetProperty("ownExpectancyHigh").GetString()).ShouldBe(("0.02", "0.58"));
        (bucket.GetProperty("badge").GetString(), bucket.GetProperty("fewOwnCases").GetBoolean()).ShouldBe(("flat", false));
        JsonElement few = ideas[1].GetProperty("bucket");
        (few.GetProperty("cases").GetInt32(), few.GetProperty("enoughData").GetBoolean(), few.GetProperty("fewOwnCases").GetBoolean()).ShouldBe((12, true, true));
        (few.GetProperty("ownProbability").ValueKind, few.GetProperty("badge").GetString()).ShouldBe((JsonValueKind.Null, "down"));
    }

    // An idea of a bucket without cases has only its own break-even; the list goes by expectancy.
    [Test]
    public async Task Ideas_without_a_bucket_have_their_own_break_even_and_come_last()
    {
        Idea withBucket = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Sol);
        Idea without = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-5), instrument: Sol, type: IdeaType.BeyondExtreme);
        await new IdeaRepository(_database).SaveAsync([without, withBucket], [], CancellationToken.None);
        await new IdeaBucketRepository(_database).ReplaceAsync(
            IdeasEngineVersion.Current,
            [new IdeaBucket(new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear), 90, BucketStatisticsCalculator.Compute(10, 30, 0, -10m, 70m))],
            null, CancellationToken.None);

        JsonElement[] ideas = [.. (await ItemsAsync("/api/ideas")).EnumerateArray()];

        ideas.Select(idea => idea.GetProperty("id").GetGuid()).ShouldBe([withBucket.Id, without.Id]);
        ideas[1].GetProperty("tick").ValueKind.ShouldBe(JsonValueKind.Null);
        ideas[1].GetProperty("bucket").ValueKind.ShouldBe(JsonValueKind.Null);
        decimal.Parse(ideas[1].GetProperty("breakEven").GetString()!, CultureInfo.InvariantCulture).ShouldBe(0.0496691m / (0.0930381m + 0.0496691m), 0.000001m);
        (await ItemsAsync("/api/ideas?nonNegativeExpectancy=true")).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Idea_is_read_by_its_id_and_the_buckets_whole()
    {
        Idea idea = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Sol, tick: 0.5m);
        await new IdeaRepository(_database).SaveAsync([idea], [], CancellationToken.None);
        var key = new IdeaBucketKey(IdeaSetup.Drop, IdeaType.Pullback, Interval.OneHour, IdeaSide.Short, MarketCategory.Spot);
        await new IdeaBucketRepository(_database).ReplaceAsync(IdeasEngineVersion.Current, [new IdeaBucket(key, 30, BucketStatisticsCalculator.Compute(6, 5, 1, 2.4m, 20m))], null, CancellationToken.None);

        JsonElement read = await GetAsync($"/api/ideas/{idea.Id}");
        (read.GetProperty("symbol").GetString(), read.GetProperty("tick").GetString()).ShouldBe(("SOLUSDT", "0.5"));
        ShouldHaveMoney(read);
        (await _client.GetAsync($"/api/ideas/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonElement bucket = (await GetAsync("/api/ideas/buckets")).EnumerateArray().Single();
        (bucket.GetProperty("setup").GetString(), bucket.GetProperty("type").GetString(), bucket.GetProperty("interval").GetString(), bucket.GetProperty("side").GetString())
            .ShouldBe(("drop", "pullback", "60", "short"));
        (bucket.GetProperty("category").GetString(), bucket.GetProperty("days").GetInt32(), bucket.GetProperty("statistics").GetProperty("targets").GetInt32()).ShouldBe(("spot", 30, 6));
    }

    // Scenario "Калибровка за 30 дней по linear": the report of the window and the category, numbers as decimal strings.
    [Test]
    public async Task Calibration_is_read_by_window_and_category()
    {
        DateTimeOffset computedAt = SnapshotAt.AddMinutes(-3);
        BucketStatistics thirty = BucketStatisticsCalculator.Compute(6, 21, 3, 6.75m, 25.3125m);
        var linear = new CalibrationReport(
            computedAt,
            30,
            MarketCategory.Linear,
            30,
            4,
            0.207m,
            0.219m,
            [new CalibrationRow(0.2m, 0.3m, null, 0.25m, thirty)],
            [new CalibrationRow(0.2m, 0.4m, null, 0.3m, thirty)],
            [new CalibrationRow(null, null, IdeaBadge.Flat, 0.3m, thirty)],
            new CalibrationRow(null, null, null, 0.3m, thirty));
        await new IdeaCalibrationRepository(_database).ReplaceAsync(
            IdeasEngineVersion.Current,
            [linear, linear with { Days = 90, Checked = 1, Total = null }, linear with { Days = 90, Category = null, Checked = 2 }],
            CancellationToken.None);

        JsonElement report = await GetAsync("/api/ideas/calibration?days=30&category=linear");

        (report.GetProperty("computedAt").GetDateTimeOffset(), report.GetProperty("days").GetInt32(), report.GetProperty("category").GetString()).ShouldBe((computedAt, 30, "linear"));
        (report.GetProperty("checked").GetInt32(), report.GetProperty("unchecked").GetInt32()).ShouldBe((30, 4));
        (report.GetProperty("brier").GetString(), report.GetProperty("baseBrier").GetString()).ShouldBe(("0.207", "0.219"));
        JsonElement row = report.GetProperty("probabilities").EnumerateArray().Single();
        (row.GetProperty("from").GetString(), row.GetProperty("to").GetString(), row.GetProperty("promised").GetString(), row.GetProperty("cases").GetInt32()).ShouldBe(("0.2", "0.3", "0.25", 30));
        (row.GetProperty("targetShare").GetString(), row.GetProperty("stopShare").GetString(), row.GetProperty("expiredShare").GetString()).ShouldBe(("0.2", "0.7", "0.1"));
        (row.GetProperty("probabilityLow").GetString(), row.GetProperty("probabilityHigh").GetString()).ShouldBe((Text(thirty.ProbabilityLow!.Value), Text(thirty.ProbabilityHigh!.Value)));
        (row.GetProperty("expectancy").GetString(), row.GetProperty("expectancyLow").GetString()).ShouldBe(("0.225", Text(thirty.ExpectancyLow!.Value)));
        report.GetProperty("expectancies")[0].GetProperty("to").GetString().ShouldBe("0.4");
        (report.GetProperty("badges")[0].GetProperty("badge").GetString(), report.GetProperty("badges")[0].GetProperty("from").ValueKind).ShouldBe(("flat", JsonValueKind.Null));
        report.GetProperty("total").GetProperty("cases").GetInt32().ShouldBe(30);
        (await GetAsync("/api/ideas/calibration")).GetProperty("checked").GetInt32().ShouldBe(2);
        JsonElement withoutTotal = await GetAsync("/api/ideas/calibration?days=90&category=linear");
        (withoutTotal.GetProperty("checked").GetInt32(), withoutTotal.GetProperty("total").ValueKind).ShouldBe((1, JsonValueKind.Null));
    }

    // Scenario "После изменения правил": the report of the scope, with the table by own data and the comparison of the models.
    [Test]
    public async Task Calibration_after_the_rules_changed_is_read_by_its_scope()
    {
        BucketStatistics thirty = BucketStatisticsCalculator.Compute(6, 21, 3, 6.75m, 25.3125m);
        var all = new CalibrationReport(SnapshotAt.AddMinutes(-3), 90, null, 30, 4, 0.207m, 0.219m, [], [], [], null);
        CalibrationReport fresh = all with
        {
            Checked = 12,
            Scope = CalibrationScope.Fresh,
            Owns = [new CalibrationOwnRow(true, new CalibrationRow(null, null, null, 0.05m, thirty), 0.21m)],
            Comparison = new CalibrationComparison(
                new CalibrationModel(30, 0.22m, new CalibrationRow(null, null, IdeaBadge.Up, 0.145m, thirty)),
                new CalibrationModel(30, 0.207m, null),
                new CalibrationModel(30, 0.201m, new CalibrationRow(null, null, IdeaBadge.Up, 0.12m, thirty)),
                new CalibrationDifference(-0.006m, -0.012m, null))
        };
        // Scenario "Отчёт без модели с признаками": a report computed before add-idea-features.
        CalibrationReport before = all with { Days = 30, Comparison = new CalibrationComparison(new CalibrationModel(30, 0.22m, null), new CalibrationModel(30, 0.207m, null)) };
        await new IdeaCalibrationRepository(_database).ReplaceAsync(IdeasEngineVersion.Current, [all, fresh, before], CancellationToken.None);

        JsonElement report = await GetAsync("/api/ideas/calibration?scope=fresh");

        (report.GetProperty("scope").GetString(), report.GetProperty("checked").GetInt32()).ShouldBe(("fresh", 12));
        JsonElement own = report.GetProperty("owns").EnumerateArray().Single();
        (own.GetProperty("fewOwnCases").GetBoolean(), own.GetProperty("brier").GetString(), own.GetProperty("promised").GetString(), own.GetProperty("cases").GetInt32())
            .ShouldBe((true, "0.21", "0.05", 30));
        JsonElement comparison = report.GetProperty("comparison");
        (comparison.GetProperty("old").GetProperty("cases").GetInt32(), comparison.GetProperty("old").GetProperty("brier").GetString()).ShouldBe((30, "0.22"));
        (comparison.GetProperty("old").GetProperty("up").GetProperty("promised").GetString(), comparison.GetProperty("new").GetProperty("up").ValueKind)
            .ShouldBe(("0.145", JsonValueKind.Null));
        JsonElement features = comparison.GetProperty("features");
        (features.GetProperty("cases").GetInt32(), features.GetProperty("brier").GetString(), features.GetProperty("up").GetProperty("promised").GetString())
            .ShouldBe((30, "0.201", "0.12"));
        JsonElement difference = comparison.GetProperty("difference");
        (difference.GetProperty("mean").GetString(), difference.GetProperty("low").GetString(), difference.GetProperty("high").ValueKind)
            .ShouldBe(("-0.006", "-0.012", JsonValueKind.Null));
        JsonElement whole = await GetAsync("/api/ideas/calibration?scope=all");
        (whole.GetProperty("scope").GetString(), whole.GetProperty("checked").GetInt32(), whole.GetProperty("comparison").ValueKind).ShouldBe(("all", 30, JsonValueKind.Null));
        JsonElement before30 = (await GetAsync("/api/ideas/calibration?days=30")).GetProperty("comparison");
        (before30.GetProperty("features").ValueKind, before30.GetProperty("difference").ValueKind).ShouldBe((JsonValueKind.Null, JsonValueKind.Null));
    }

    // Scenario "Разбивки в ответе": the breakdowns by interval and by type with the codes, the rows and the comparison of each row.
    [Test]
    public async Task Calibration_has_the_breakdowns_by_interval_and_by_type()
    {
        BucketStatistics thirty = BucketStatisticsCalculator.Compute(6, 21, 3, 6.75m, 25.3125m);
        var empty = new CalibrationComparison(new CalibrationModel(0, null, null), new CalibrationModel(0, null, null));
        CalibrationReport report = new CalibrationReport(SnapshotAt.AddMinutes(-3), 90, null, 60, 0, 0.2m, 0.21m, [], [], [], new CalibrationRow(null, null, null, 0.1m, thirty)) with
        {
            Intervals =
            [
                new CalibrationSlice(Interval.FiveMinutes, null, new CalibrationRow(null, null, null, -0.05m, thirty), null, empty),
                new CalibrationSlice(
                    Interval.OneHour,
                    null,
                    new CalibrationRow(null, null, null, 0.02m, thirty),
                    new CalibrationRow(null, null, IdeaBadge.Up, 0.15m, thirty),
                    new CalibrationComparison(new CalibrationModel(30, 0.22m, new CalibrationRow(null, null, IdeaBadge.Up, 0.145m, thirty)), new CalibrationModel(30, 0.207m, null)))
            ],
            Types =
            [
                new CalibrationSlice(null, IdeaType.TwoR, new CalibrationRow(null, null, null, 0.1m, thirty), null, empty),
                new CalibrationSlice(null, IdeaType.Pullback, new CalibrationRow(null, null, null, 0.1m, thirty), null, empty)
            ],
            Features =
            [
                new CalibrationSlice(null, null, new CalibrationRow(null, null, null, -0.2m, thirty), null, empty, IdeaFeature.DayDrop, true),
                new CalibrationSlice(null, null, new CalibrationRow(null, null, null, 0.05m, thirty), null, empty, IdeaFeature.DayDrop, false)
            ]
        };
        await new IdeaCalibrationRepository(_database).ReplaceAsync(IdeasEngineVersion.Current, [report], CancellationToken.None);

        JsonElement read = await GetAsync("/api/ideas/calibration");

        JsonElement[] intervals = [.. read.GetProperty("intervals").EnumerateArray()];
        intervals.Select(slice => (slice.GetProperty("interval").GetString(), slice.GetProperty("type").ValueKind)).ShouldBe([("5", JsonValueKind.Null), ("60", JsonValueKind.Null)]);
        (intervals[0].GetProperty("total").GetProperty("promised").GetString(), intervals[0].GetProperty("up").ValueKind).ShouldBe(("-0.05", JsonValueKind.Null));
        (intervals[1].GetProperty("total").GetProperty("cases").GetInt32(), intervals[1].GetProperty("up").GetProperty("promised").GetString(), intervals[1].GetProperty("up").GetProperty("badge").GetString())
            .ShouldBe((30, "0.15", "up"));
        JsonElement comparison = intervals[1].GetProperty("comparison");
        (comparison.GetProperty("old").GetProperty("up").GetProperty("promised").GetString(), comparison.GetProperty("new").GetProperty("brier").GetString()).ShouldBe(("0.145", "0.207"));
        read.GetProperty("types").EnumerateArray().Select(slice => (slice.GetProperty("type").GetString(), slice.GetProperty("interval").ValueKind))
            .ShouldBe([("target_2r", JsonValueKind.Null), ("pullback", JsonValueKind.Null)]);
        // Scenario "Разбивка в ответе": each row of the breakdown by feature has its feature, whether its cases have it, and the numbers of a row.
        read.GetProperty("features").EnumerateArray()
            .Select(slice => (slice.GetProperty("feature").GetString(), slice.GetProperty("present").GetBoolean(), slice.GetProperty("total").GetProperty("promised").GetString(), slice.GetProperty("total").GetProperty("cases").GetInt32()))
            .ShouldBe([("day_drop", true, "-0.2", 30), ("day_drop", false, "0.05", 30)]);
        intervals[0].GetProperty("feature").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // Scenario "Ещё не посчитана".
    [Test]
    public async Task Calibration_before_the_first_one_is_empty()
    {
        JsonElement report = await GetAsync("/api/ideas/calibration?category=spot");

        (report.GetProperty("computedAt").ValueKind, report.GetProperty("days").GetInt32(), report.GetProperty("category").GetString()).ShouldBe((JsonValueKind.Null, 90, "spot"));
        (report.GetProperty("checked").GetInt32(), report.GetProperty("unchecked").GetInt32(), report.GetProperty("brier").ValueKind).ShouldBe((0, 0, JsonValueKind.Null));
        (report.GetProperty("probabilities").GetArrayLength(), report.GetProperty("badges").GetArrayLength(), report.GetProperty("total").ValueKind).ShouldBe((0, 0, JsonValueKind.Null));
        (report.GetProperty("scope").GetString(), report.GetProperty("owns").GetArrayLength(), report.GetProperty("comparison").ValueKind).ShouldBe(("all", 0, JsonValueKind.Null));
        (report.GetProperty("intervals").GetArrayLength(), report.GetProperty("types").GetArrayLength(), report.GetProperty("features").GetArrayLength()).ShouldBe((0, 0, 0));
    }

    // Scenarios "Другое окно" and "Другой режим".
    [TestCase("days=60", "days")]
    [TestCase("category=inverse", "category")]
    [TestCase("scope=old", "scope")]
    public async Task Calibration_of_another_window_or_category_is_refused(string query, string key)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/ideas/calibration?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").TryGetProperty(key, out _).ShouldBeTrue();
    }

    [TestCase("setup=drop", "drop")]
    [TestCase("interval=60", "hour")]
    [TestCase("type=pullback", "pullback")]
    [TestCase("category=spot", "spot")]
    [TestCase("side=short", "drop")]
    public async Task Ideas_are_filtered_by_each_field(string query, string expected)
    {
        Idea impulse = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Sol);
        var ideas = new Dictionary<string, Idea>
        {
            ["drop"] = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-19), instrument: Sol, setup: IdeaSetup.Drop, side: IdeaSide.Short),
            ["hour"] = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-18), instrument: Sol, interval: Interval.OneHour),
            ["pullback"] = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-17), instrument: Sol, type: IdeaType.Pullback),
            ["spot"] = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-16), instrument: Eth)
        };
        await new IdeaRepository(_database).SaveAsync([impulse, .. ideas.Values], [], CancellationToken.None);

        (await ItemsAsync($"/api/ideas?{query}")).EnumerateArray().Select(idea => idea.GetProperty("id").GetGuid()).ShouldBe([ideas[expected].Id]);
    }

    // Scenarios "Только активные" and "Скрыть акции": each flag drops its ideas, the others stay.
    [TestCase("activeOnly=true", "closed")]
    [TestCase("hideStocks=true", "stock")]
    public async Task Ideas_are_dropped_by_each_flag(string query, string dropped)
    {
        var ideas = new Dictionary<string, Idea>
        {
            ["crypto"] = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Sol),
            ["closed"] = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-19), IdeaStatus.Target, new Instrument(MarketCategory.Linear, "BTCUSDT")),
            ["stock"] = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-18), instrument: new Instrument(MarketCategory.Linear, "AAPLUSDT"))
        };
        await new IdeaRepository(_database).SaveAsync([.. ideas.Values], [], CancellationToken.None);
        await new ScannerPairRepository(_database).SaveAsync(
            [new ScannerPair(Sol, true), new ScannerPair(ideas["stock"].Instrument, true, TradFi: true)],
            CancellationToken.None);

        (await ItemsAsync($"/api/ideas?{query}")).EnumerateArray().Select(idea => idea.GetProperty("id").GetGuid())
            .ShouldBe([.. ideas.Where(idea => idea.Key != dropped).Select(idea => idea.Value.Id)], ignoreOrder: true);
    }

    // Scenario "Вместе с другим фильтром".
    [Test]
    public async Task Flags_go_with_the_other_filters()
    {
        Idea wanted = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), IdeaStatus.Active, Sol);
        Idea shortIdea = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-19), instrument: new Instrument(MarketCategory.Linear, "ETHUSDT"), side: IdeaSide.Short);
        Idea closed = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-18), IdeaStatus.Stop, new Instrument(MarketCategory.Linear, "BTCUSDT"));
        Idea stock = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-17), instrument: new Instrument(MarketCategory.Linear, "AAPLUSDT"));
        await new IdeaRepository(_database).SaveAsync([wanted, shortIdea, closed, stock], [], CancellationToken.None);
        await new ScannerPairRepository(_database).SaveAsync([new ScannerPair(stock.Instrument, true, TradFi: true)], CancellationToken.None);

        JsonElement page = await GetAsync("/api/ideas?side=long&activeOnly=true&hideStocks=true");

        page.GetProperty("items").EnumerateArray().Select(idea => idea.GetProperty("id").GetGuid()).ShouldBe([wanted.Id]);
        page.GetProperty("total").GetInt32().ShouldBe(1);
    }

    // Scenario "Страница идей": 120 ideas without a bucket come newest first.
    [Test]
    public async Task The_third_page_of_50_holds_ideas_101_to_120_with_the_page_number_and_the_total()
    {
        Idea[] ideas = await SaveIdeasAsync(120);

        JsonElement page = await GetAsync("/api/ideas?page=3");

        page.GetProperty("items").EnumerateArray().Select(idea => idea.GetProperty("id").GetGuid()).ShouldBe([.. ideas.Skip(100).Select(idea => idea.Id)]);
        (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32(), page.GetProperty("total").GetInt32()).ShouldBe((3, 50, 120));
    }

    // Scenario "Страница за концом списка".
    [Test]
    public async Task A_page_past_the_end_gives_the_last_page()
    {
        await SaveIdeasAsync(30);

        JsonElement page = await GetAsync("/api/ideas?page=2");

        (page.GetProperty("items").GetArrayLength(), page.GetProperty("page").GetInt32(), page.GetProperty("total").GetInt32()).ShouldBe((30, 1, 30));
    }

    // Scenario "Сортировка по всем страницам".
    [Test]
    public async Task A_column_orders_all_the_ideas_before_the_page_is_cut()
    {
        Idea[] ideas = await SaveIdeasAsync(60);

        JsonElement page = await GetAsync("/api/ideas?sort=firedAt&direction=asc");

        page.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(ideas[^1].Id);
    }

    // Scenario "Неизвестный размер страницы", and an interval the ideas do not keep. The other wrong parameters are in GetIdeasTests.
    [TestCase("pageSize=75", "pageSize")]
    [TestCase("interval=30", "interval")]
    public async Task A_wrong_parameter_is_a_validation_error_by_the_parameter(string query, string key)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/ideas?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").TryGetProperty(key, out _).ShouldBeTrue();
    }

    // Scenario "Сохранённый фильтр в запросе".
    [Test]
    public async Task A_saved_filter_keeps_the_ideas_with_that_expectancy_whose_pair_has_that_turnover()
    {
        await SnapshotAsync(Sol, 100m, turnover24h: 60000000m);
        await SnapshotAsync(Eth, 3000m, turnover24h: 1000000m);
        Idea rich = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Sol);
        Idea poor = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-19), instrument: Eth);
        await new IdeaRepository(_database).SaveAsync([rich, poor], [], CancellationToken.None);
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        await new IdeaBucketRepository(_database).ReplaceAsync(
            IdeasEngineVersion.Current,
            [
                new IdeaBucket(key, 90, BucketStatisticsCalculator.Compute(180, 200, 20, 100m, 1000m)),
                new IdeaBucket(key with { Category = MarketCategory.Spot }, 90, BucketStatisticsCalculator.Compute(180, 200, 20, 100m, 1000m))
            ],
            null, CancellationToken.None);
        HttpResponseMessage created = await _client.PostAsJsonAsync(
            "/api/saved-filters",
            new
            {
                name = "Rich",
                place = "ideas",
                conditions = new[]
                {
                    new { field = "expectancy", @operator = ">", value = "0.2", to = (string?)null },
                    new { field = "turnover24h", @operator = ">", value = "50000000", to = (string?)null }
                }
            });
        Guid filter = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        JsonElement page = await GetAsync($"/api/ideas?savedFilter={filter}");

        page.GetProperty("items").EnumerateArray().Select(idea => idea.GetProperty("id").GetGuid()).ShouldBe([rich.Id]);
        page.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task Ideas_of_a_pair_are_its_own()
    {
        Idea ofSol = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Sol, tick: 0.01m);
        await new IdeaRepository(_database).SaveAsync([ofSol, IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Eth)], [], CancellationToken.None);

        JsonElement[] ofPair = [.. (await GetAsync("/api/market/linear/SOLUSDT/ideas")).EnumerateArray()];
        ofPair.Select(idea => idea.GetProperty("id").GetGuid()).ShouldBe([ofSol.Id]);
        ofPair[0].GetProperty("tick").GetString().ShouldBe("0.01");
        ShouldHaveMoney(ofPair[0]);
        (await _client.GetAsync("/api/market/inverse/SOLUSD/ideas")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // Scenario "Два сетапа на 15 минутах"; a pair without setups lists every interval empty.
    [Test]
    public async Task Row_of_the_snapshot_has_the_setups_of_the_pair_by_interval()
    {
        await SnapshotAsync(Sol, 100.5m);
        await SnapshotAsync(Eth, 3000m);
        await new MarketSnapshotRepository(_database).ReplaceSetupsAsync(
            _holder,
            new Dictionary<Instrument, IReadOnlyList<PairSetup>> { [Sol] = [new PairSetup(Interval.FifteenMinutes, IdeaSetup.Impulse), new PairSetup(Interval.FifteenMinutes, IdeaSetup.HighVolatility)] },
            CancellationToken.None);

        JsonElement[] rows = [.. (await GetAsync("/api/market")).GetProperty("items").EnumerateArray()];
        JsonElement sol = rows.Single(row => row.GetProperty("symbol").GetString() == "SOLUSDT").GetProperty("setups");
        JsonElement eth = rows.Single(row => row.GetProperty("symbol").GetString() == "ETHUSDT").GetProperty("setups");

        sol.GetProperty("15").EnumerateArray().Select(setup => setup.GetString()).ShouldBe(["impulse", "high_volatility"], ignoreOrder: true);
        sol.GetProperty("5").GetArrayLength().ShouldBe(0);
        new[] { "5", "15", "60", "240" }.ShouldAllBe(interval => eth.GetProperty(interval).GetArrayLength() == 0);
        (await GetAsync("/api/market/linear/SOLUSDT")).GetProperty("setups").GetProperty("15").GetArrayLength().ShouldBe(2);
    }

    // Scenario "Фильтр по новому сетапу и типу": the list keeps only the stop entries of a squeeze; the market tells the candle setups of the
    // pair, counts them for the chips and keeps the pairs with a breakout on the hour.
    [Test]
    public async Task Ideas_and_market_take_the_codes_of_the_candle_setups()
    {
        Idea squeeze = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-10), instrument: Sol, setup: IdeaSetup.Squeeze, type: IdeaType.StopEntry, interval: Interval.OneHour);
        Idea impulse = IdeaGenerator.Generate(SnapshotAt.AddMinutes(-20), instrument: Sol);
        await new IdeaRepository(_database).SaveAsync([squeeze, impulse], [], CancellationToken.None);
        await SnapshotAsync(Sol, 100.5m);
        await SnapshotAsync(Eth, 3000m);
        await new MarketSnapshotRepository(_database).ReplaceSetupsAsync(
            _holder,
            new Dictionary<Instrument, IReadOnlyList<PairSetup>> { [Sol] = [new PairSetup(Interval.OneHour, IdeaSetup.Squeeze), new PairSetup(Interval.OneHour, IdeaSetup.Breakout)] },
            CancellationToken.None);

        JsonElement idea = (await ItemsAsync("/api/ideas?setup=squeeze&type=stop_entry")).EnumerateArray().Single();
        JsonElement market = await GetAsync("/api/market?setup=breakout&interval=60");

        (idea.GetProperty("id").GetGuid(), idea.GetProperty("setup").GetString(), idea.GetProperty("type").GetString()).ShouldBe((squeeze.Id, "squeeze", "stop_entry"));
        JsonElement row = market.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("symbol").GetString().ShouldBe("SOLUSDT");
        row.GetProperty("setups").GetProperty("60").EnumerateArray().Select(setup => setup.GetString()).ShouldBe(["squeeze", "breakout"], ignoreOrder: true);
        (market.GetProperty("setups").GetProperty("breakout").GetInt32(), market.GetProperty("setups").GetProperty("quiet_volume").GetInt32()).ShouldBe((1, 0));
    }

    // An unknown setup or type is answered with every code the API takes, the candle setups and «Стоп-вход» among them.
    [Test]
    public async Task Unknown_setup_and_type_name_every_code()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/ideas?setup=foo&type=foo");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        errors.GetProperty("setup")[0].GetString().ShouldBe(
            "Setup must be impulse, early_impulse, drop, early_drop, high_volatility, breakout, false_breakout, squeeze, exhaustion, rsi_extreme, new_extreme, quiet_volume or accumulation.");
        errors.GetProperty("type")[0].GetString().ShouldBe("Type must be levels, target_2r, beyond_extreme, pullback or stop_entry.");
    }

    // Scenario "Своя идея «Стоп-вход»": without a setup a stop entry has no range.
    [Test]
    public async Task Own_stop_entry_is_a_validation_error()
    {
        await MinutesAsync();
        await SnapshotAsync(Sol, 100.5m);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/market/linear/SOLUSDT/own-idea", new { type = "stop_entry", interval = "60", side = "long" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").GetProperty("type")[0].GetString()
            .ShouldBe("A stop entry comes only with a setup: without one it has no range.");
    }

    // Scenario "Своя идея short": the stop above the nearest resistance, 105.0–105.2, with k × ATR, the target at 2R, the break-even after the
    // fees above the 1 / 3 of 2R without them.
    [Test]
    public async Task Own_idea_is_measured_from_the_price_now_by_the_rules_of_its_type()
    {
        IReadOnlyList<Candle> minutes = await MinutesAsync();
        await SnapshotAsync(Sol, 100.5m);
        decimal atr = Atr.Compute(Hours(minutes))!.Value;

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/market/linear/SOLUSDT/own-idea", new { type = "target_2r", interval = "60", side = "short" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement idea = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        // The stop goes on the price step of the pair away from the entry.
        decimal stop = Math.Ceiling((105.2m + (1.5m * atr)) / 0.01m) * 0.01m;
        idea.GetProperty("made").GetBoolean().ShouldBeTrue();
        (idea.GetProperty("entry").GetString(), idea.GetProperty("stop").GetString(), idea.GetProperty("target").GetString())
            .ShouldBe(("100.5", Text(stop), Text(100.5m - (2m * (stop - 100.5m)))));
        idea.GetProperty("riskReward").GetString().ShouldBe("2");
        // Scenario "Своя идея": the R:R and the break-even after the fees and the slippage of linear, a market entry.
        decimal target = 100.5m - (2m * (stop - 100.5m));
        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Short, 100.5m, stop, target), new FeeRates(0.02m, 0.055m, 0.05m));
        idea.GetProperty("netRiskReward").GetString().ShouldBe(Text(shares.NetRiskReward));
        idea.GetProperty("breakEven").GetString().ShouldBe(Text(shares.BreakEven!.Value));
        shares.BreakEven.Value.ShouldBeGreaterThan(1m / 3m);
        idea.GetProperty("tick").GetString().ShouldBe("0.01");
    }

    // Without the zone it needs the rules make no idea.
    [Test]
    public async Task Own_idea_without_its_zone_is_not_made()
    {
        await MinutesAsync();
        await SnapshotAsync(Sol, 100.5m);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/market/linear/SOLUSDT/own-idea", new { type = "levels", interval = "60", side = "short" });

        JsonElement idea = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        (idea.GetProperty("made").GetBoolean(), idea.GetProperty("entry").ValueKind).ShouldBe((false, JsonValueKind.Null));
    }

    // Without 14 closed candles there is no ATR to measure by: no idea and no zones.
    [Test]
    public async Task Pair_without_history_has_no_own_idea_and_no_zones()
    {
        await SnapshotAsync(Sol, 100.5m);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/market/linear/SOLUSDT/own-idea", new { type = "beyond_extreme", interval = "15", side = "long" });

        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("made").GetBoolean().ShouldBeFalse();
        (await GetAsync("/api/market/linear/SOLUSDT/zones?interval=15")).GetProperty("zones").GetArrayLength().ShouldBe(0);
    }

    // A pair whose price step the list of instruments has not told yet has no own idea.
    [Test]
    public async Task Own_idea_of_a_pair_without_its_price_step_is_not_made()
    {
        await MinutesAsync();
        await SnapshotAsync(Sol, 100.5m, tick: null);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/market/linear/SOLUSDT/own-idea", new { type = "target_2r", interval = "60", side = "short" });

        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("made").GetBoolean().ShouldBeFalse();
    }

    // Scenario "Своя идея на spot".
    [Test]
    public async Task Own_short_idea_on_spot_is_a_validation_error()
    {
        await SnapshotAsync(Eth, 3000m);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/market/spot/ETHUSDT/own-idea", new { type = "target_2r", interval = "60", side = "short" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").GetProperty("side")[0].GetString().ShouldBe("Spot has no short ideas.");
    }

    [Test]
    public async Task Own_idea_needs_known_codes_and_a_pair_of_the_snapshot()
    {
        (await _client.PostAsJsonAsync("/api/market/linear/SOLUSDT/own-idea", new { type = "scalp", interval = "30", side = "up" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.PostAsJsonAsync("/api/market/linear/NOPEUSDT/own-idea", new { type = "target_2r", interval = "60", side = "long" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Zones_of_an_interval_are_told_against_the_price()
    {
        await MinutesAsync();
        await SnapshotAsync(Sol, 100.5m);

        JsonElement zones = await GetAsync("/api/market/linear/SOLUSDT/zones?interval=60");

        zones.GetProperty("price").GetString().ShouldBe("100.5");
        JsonElement zone = zones.GetProperty("zones").EnumerateArray().Single();
        (zone.GetProperty("low").GetString(), zone.GetProperty("high").GetString(), zone.GetProperty("strength").GetInt32(), zone.GetProperty("kind").GetString())
            .ShouldBe(("105.0", "105.2", 2, "resistance"));
        (await _client.GetAsync("/api/market/linear/SOLUSDT/zones?interval=30")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.GetAsync("/api/market/inverse/SOLUSD/zones?interval=60")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.GetAsync("/api/market/linear/NOPEUSDT/zones?interval=60")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestCase("105.1", "around")]
    [TestCase("106", "support")]
    public async Task Zone_around_the_price_or_below_it_is_told_so(string price, string kind)
    {
        await MinutesAsync();
        await SnapshotAsync(Sol, decimal.Parse(price, CultureInfo.InvariantCulture));

        (await GetAsync("/api/market/linear/SOLUSDT/zones?interval=60")).GetProperty("zones")[0].GetProperty("kind").GetString().ShouldBe(kind);
    }

    // 30 hours of SOLUSDT before 11:00 at 100 with a high of 101 and a low of 99 a hour, but highs of 105.0 and 105.2 in the hours of
    // 15:00 and 01:00; then flat at 100.5 up to the snapshot.
    private async Task<IReadOnlyList<Candle>> MinutesAsync()
    {
        var from = DateTimeOffset.Parse("2024-03-09T05:00:00Z", CultureInfo.InvariantCulture);
        var hoursEnd = DateTimeOffset.Parse("2024-03-10T11:00:00Z", CultureInfo.InvariantCulture);
        var minutes = new List<Candle>();

        for (DateTimeOffset time = from; time < SnapshotAt.AddSeconds(-10); time = time.AddMinutes(1))
        {
            int hour = (int)(time - from).TotalHours;
            decimal high = time.Minute == 30 ? hour == 10 ? 105.0m : hour == 20 ? 105.2m : 101m : 100m;
            decimal low = time.Minute == 31 ? 99m : 100m;
            minutes.Add(time < hoursEnd ? Candle(time, 100m, high, low, 100m) : Candle(time, 100.5m, 100.5m, 100.5m, 100.5m));
        }

        await new CandleRepository(_database).UpsertAsync(Sol, Interval.OneMinute, minutes, CancellationToken.None);

        return minutes;
    }

    private static IReadOnlyList<IdeaCandle> Hours(IReadOnlyList<Candle> minutes)
    {
        IdeaCandle[] ideaMinutes = [.. minutes.Select(candle => new IdeaCandle(candle.OpenTime, candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Turnover.Value))];

        return IdeaCandles.FromMinutes(Interval.OneHour, ideaMinutes, ideaMinutes[0].OpenTime, DateTimeOffset.Parse("2024-03-10T11:00:00Z", CultureInfo.InvariantCulture));
    }

    // The pair of the scanner with its price step, which the list of instruments told.
    private async Task SnapshotAsync(Instrument instrument, decimal price, decimal? tick = 0.01m, decimal? turnover24h = null)
    {
        await new ScannerPairRepository(_database).SaveAsync([new ScannerPair(instrument, true, TickSize: tick)], CancellationToken.None);
        var store = new MarketSnapshotRepository(_database);
        MarketSnapshotRow[] rows =
        [
            .. (await store.GetAllAsync(CancellationToken.None)),
            new MarketSnapshotRow(instrument, SnapshotAt, new PairMetrics(price, null, null, null, null, null, null, turnover24h, null, null, null, null, 0.01m), false)
        ];
        await store.ReplaceAsync(_holder, rows, SnapshotAt, CancellationToken.None);
    }

    private static Candle Candle(DateTimeOffset time, decimal open, decimal high, decimal low, decimal close)
    {
        return new Candle(time, new Price(open), new Price(high), new Price(low), new Price(close), new Volume(1m), new Volume(100m));
    }

    // Scenario "Деньги на 1 USDT суммы" for the idea of the generator on linear (entry 105, stop 100, target 115, taker 0.055 %, slippage
    // 0.05 %): the entry buys at 105.0525, the target sells at 114.9425 and the stop at 99.95, the fees on those prices:
    // (9.89 − 0.0577789 − 0.0632184) / 105, (5.1025 + 0.0577789 + 0.0549725) / 105, 5 / 105, and the first over the second.
    private static void ShouldHaveMoney(JsonElement idea)
    {
        decimal Share(string name) => decimal.Parse(idea.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

        Share("profitPerUsdt").ShouldBe(0.0930381m, 0.0000001m);
        Share("lossPerUsdt").ShouldBe(0.0496691m, 0.0000001m);
        Share("riskPerUsdt").ShouldBe(0.0476190m, 0.0000001m);
        // Scenario "R:R идеи после комиссий": the profit over the loss, not the 2 of the prices.
        Share("netRiskReward").ShouldBe(1.8732m, 0.0001m);
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // Ideas of a different pair each, so none replaces another; the first is the newest.
    private async Task<Idea[]> SaveIdeasAsync(int count)
    {
        Idea[] ideas = [.. Enumerable.Range(0, count).Select(number => IdeaGenerator.Generate(SnapshotAt.AddSeconds(-number), instrument: new Instrument(MarketCategory.Linear, $"S{number:000}USDT")))];
        await new IdeaRepository(_database).SaveAsync(ideas, [], CancellationToken.None);

        return ideas;
    }

    private async Task<JsonElement> ItemsAsync(string path)
    {
        return (await GetAsync(path)).GetProperty("items");
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        HttpResponseMessage response = await _client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}