using System.Globalization;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Ideas.Queries.GetOwnIdea;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Ideas;

/// <summary>Requirement "Своя идея": the levels of an idea from the price now and its money after the fees of the category.</summary>
public class GetOwnIdeaTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private ICandleRepository _candles = null!;
    private GetOwnIdeaQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        IMarketSnapshotRepository snapshots = Substitute.For<IMarketSnapshotRepository>();
        IScannerPairRepository pairs = Substitute.For<IScannerPairRepository>();
        _candles = Substitute.For<ICandleRepository>();
        snapshots.GetAsync(Sol, Arg.Any<CancellationToken>())
            .Returns(MarketSnapshotRowGenerator.Generate("SOLUSDT", metrics: MarketSnapshotRowGenerator.NoMetrics with { Price = 100m }));
        pairs.GetAsync(Sol, Arg.Any<CancellationToken>()).Returns(new ScannerPair(Sol, true, TickSize: 0.01m));
        // Thirty hours, each from 98 to 102, so ATR is 4.
        _candles.GetAggregatedAsync(Sol, Interval.OneHour, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([.. Enumerable.Range(1, 30).Select(hours => new IdeaCandle(Noon.AddHours(-hours), 100m, 102m, 98m, 100m, 1m)).Reverse()]);
        var backtests = new BacktestSettings
        {
            DefaultFees = new Dictionary<string, FeeRateSettings>
            {
                ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m }
            }
        };
        _handler = new GetOwnIdeaQueryHandler(snapshots, pairs, _candles, new IdeasSettings(), backtests);
    }

    // Scenario "Своя идея": the own idea tells the reward to risk and the break-even after the fees of its category, below the 2 of the prices
    // and above the 33 % of 2R without fees.
    [Test]
    public async Task The_own_idea_tells_its_reward_to_risk_after_the_fees()
    {
        GetOwnIdeaResult result = await _handler.Handle(new GetOwnIdeaQuery("linear", "SOLUSDT", "beyond_extreme", "60", "long"), CancellationToken.None);

        result.Plan!.RiskReward.ShouldBe(2m);
        result.Shares.ShouldBe(IdeaMoney.PerUsdt(result.Plan, new FeeRates(0.02m, 0.055m)));
        result.Shares!.NetRiskReward.ShouldBeLessThan(2m);
        result.Shares.BreakEven!.Value.ShouldBeGreaterThan(1m / 3m);
    }
}