using ProfitFox.Application.IdeaTrades;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Presentation.Contracts.IdeaTrades;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>The answer of <c>GET /api/ideas/{id}/entry</c> built from the entry as it would be made now.</summary>
public class IdeaEntryResponseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    // Review of add-idea-trade-leverage: a reason named twice, by the entry and by the checks of an auto, has the detail it was named with first.
    [Test]
    public void Reason_named_twice_has_the_detail_it_was_named_with_first()
    {
        EntryCalculation entry = Entry(
            new AutoRefusal(IdeaTradeRefusal.RiskRewardTooLow, RefusalDetail.Numbers(1.1m, 1.5m)),
            new AutoRefusal(IdeaTradeRefusal.RiskRewardTooLow, RefusalDetail.Numbers(1.1m, 1.2m)));

        var response = IdeaEntryResponse.Of(entry);

        response.Details["risk_reward_too_low"]!.Limit.ShouldBe("1.5");
    }

    private static EntryCalculation Entry(params AutoRefusal[] refusals)
    {
        Idea idea = IdeaGenerator.Generate(Now);
        var rules = new InstrumentRules(0.01m, 0.01m, 0.01m, 5m);
        var preparation = new EntryPreparation(idea, IdeaTradingSettings.Default, null, null, AccountAccess.NoKey, 100m, 100m, rules, new FeeRates(0.02m, 0.055m), 50m, "USDT", "BTC", []);
        var size = new IdeaTradeSize(0.5m, 50m, 1m, 2m, 2m);

        return new EntryCalculation(preparation, idea.Track.Plan, size, null, refusals, new IdeaPromise(null, null, null, null, 0.35m, 1.8m), 1, null);
    }
}