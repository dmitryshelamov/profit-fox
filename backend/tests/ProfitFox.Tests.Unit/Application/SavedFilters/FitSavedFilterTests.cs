using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.SavedFilters;
using ProfitFox.Application.SavedFilters.Queries.FitSavedFilter;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.SavedFilters;

/// <summary>Requirement "Подгонка черновика фильтра".</summary>
public class FitSavedFilterTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private IMarketSnapshotRepository _snapshots = null!;
    private IIdeaRepository _ideas = null!;
    private IIdeaBucketRepository _buckets = null!;
    private FitSavedFilterQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _snapshots = Substitute.For<IMarketSnapshotRepository>();
        _ideas = Substitute.For<IIdeaRepository>();
        _buckets = Substitute.For<IIdeaBucketRepository>();
        _snapshots.GetSetupsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Instrument, IReadOnlyList<PairSetup>>());
        var backtests = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };
        _handler = new FitSavedFilterQueryHandler(_snapshots, _ideas, _buckets, new IdeasEngineVersion(IdeasEngineVersion.Current), backtests, new FakeTimeProvider(Noon));
    }

    // Scenario "Условие на поле идеи": the pairs are counted without the conditions on idea fields, the ideas with them.
    [Test]
    public async Task A_condition_on_an_idea_field_cuts_the_ideas_and_not_the_pairs()
    {
        _snapshots.GetAllAsync(Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(0, 5).Select(number => MarketSnapshotRowGenerator.Generate($"P{number}USDT"))]);
        Idea good = IdeaGenerator.Generate(Noon.AddHours(-1), instrument: Sol, setup: IdeaSetup.Impulse);
        Idea bad = IdeaGenerator.Generate(Noon.AddHours(-1), instrument: Sol, setup: IdeaSetup.Drop);
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([good, bad]);
        _buckets.GetAsync(IdeasEngineVersion.Current, Arg.Any<CancellationToken>()).Returns([Bucket(good, 0.4m), Bucket(bad, 0.1m)]);

        FitSavedFilterResult result = await _handler.Handle(new FitSavedFilterQuery([new SavedFilterConditionInput("expectancy", ">=", "0.3", null)]), CancellationToken.None);

        result.ShouldBe(new FitSavedFilterResult(5, 1));
    }

    [Test]
    public async Task A_condition_on_a_metric_of_the_market_cuts_the_pairs_and_the_ideas_of_the_pairs_that_fail()
    {
        _snapshots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            MarketSnapshotRowGenerator.Generate("SOLUSDT", metrics: MarketSnapshotRowGenerator.NoMetrics with { Turnover24h = 60000000m }),
            MarketSnapshotRowGenerator.Generate("ARMUSDT", metrics: MarketSnapshotRowGenerator.NoMetrics with { Turnover24h = 1m })
        ]);
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns(
        [
            IdeaGenerator.Generate(Noon.AddHours(-1), instrument: Sol),
            IdeaGenerator.Generate(Noon.AddHours(-1), instrument: new Instrument(MarketCategory.Linear, "ARMUSDT")),
            IdeaGenerator.Generate(Noon.AddHours(-1), instrument: new Instrument(MarketCategory.Linear, "WIFUSDT"))
        ]);
        _buckets.GetAsync(IdeasEngineVersion.Current, Arg.Any<CancellationToken>()).Returns([]);

        FitSavedFilterResult result = await _handler.Handle(new FitSavedFilterQuery([new SavedFilterConditionInput("turnover24h", ">", "50000000", null)]), CancellationToken.None);

        result.ShouldBe(new FitSavedFilterResult(1, 1));
    }

    [Test]
    public async Task A_draft_without_conditions_fits_every_pair_and_every_idea()
    {
        _snapshots.GetAllAsync(Arg.Any<CancellationToken>()).Returns([MarketSnapshotRowGenerator.Generate("AAAUSDT"), MarketSnapshotRowGenerator.Generate("BBBUSDT")]);
        _ideas.GetRecentAsync(Noon.AddHours(-24), null, Arg.Any<CancellationToken>()).Returns([IdeaGenerator.Generate(Noon.AddHours(-1)), IdeaGenerator.Generate(Noon.AddHours(-2), IdeaStatus.Active)]);
        _buckets.GetAsync(IdeasEngineVersion.Current, Arg.Any<CancellationToken>()).Returns([]);

        (await _handler.Handle(new FitSavedFilterQuery([]), CancellationToken.None)).ShouldBe(new FitSavedFilterResult(2, 2));
    }

    [Test]
    public async Task A_wrong_condition_is_a_validation_error_like_the_one_of_saving()
    {
        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(
            () => _handler.Handle(new FitSavedFilterQuery([new SavedFilterConditionInput("nope", ">", "1", null)]), CancellationToken.None));

        exception.Errors.Keys.ShouldBe(["conditions[0].field"]);
    }

    // The bucket of an idea with the expectancy given after costs and without them: the condition reads that of the idea, less its costs.
    private static IdeaBucket Bucket(Idea idea, decimal expectancy)
    {
        var key = new IdeaBucketKey(idea.Setup, idea.Track.Plan.Type, idea.Interval, idea.Track.Plan.Side, idea.Instrument.Category);

        return new IdeaBucket(key, 90, IdeaViewGenerator.Bucket(expectancy, 0.5m) with { GrossExpectancy = expectancy });
    }
}