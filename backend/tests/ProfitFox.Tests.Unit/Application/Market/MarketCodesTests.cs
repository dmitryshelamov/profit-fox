using ProfitFox.Application.Market;
using ProfitFox.Domain.Entities.Market.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Market;

public class MarketCodesTests
{
    [TestCase("long", LiquidationSide.Long)]
    [TestCase("short", LiquidationSide.Short)]
    public void Liquidation_side_code_maps_both_ways(string code, LiquidationSide side)
    {
        MarketCodes.TryParseLiquidationSide(code, out LiquidationSide parsed).ShouldBeTrue();
        parsed.ShouldBe(side);
        side.ToCode().ShouldBe(code);
    }

    [Test]
    public void Order_side_is_not_a_liquidation_side()
    {
        MarketCodes.TryParseLiquidationSide("Sell", out _).ShouldBeFalse();
    }

    [TestCase(TradeSide.Buy, "buy")]
    [TestCase(TradeSide.Sell, "sell")]
    public void Trade_side_has_a_code(TradeSide side, string code)
    {
        side.ToCode().ShouldBe(code);
    }
}