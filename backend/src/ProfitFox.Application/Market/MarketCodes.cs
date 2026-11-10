using ProfitFox.Domain.Entities.Market.Enums;

namespace ProfitFox.Application.Market;

/// <summary>Codes of the sides of a liquidated position in stored rows and metrics: <c>long</c>, <c>short</c>; of the side of the taker of a trade: <c>buy</c>, <c>sell</c>.</summary>
public static class MarketCodes
{
    private static readonly Dictionary<string, LiquidationSide> SidesByCode = new Dictionary<string, LiquidationSide>(StringComparer.Ordinal)
    {
        ["long"] = LiquidationSide.Long,
        ["short"] = LiquidationSide.Short
    };

    public static bool TryParseLiquidationSide(string code, out LiquidationSide side)
    {
        return SidesByCode.TryGetValue(code, out side);
    }

    public static string ToCode(this LiquidationSide side)
    {
        return SidesByCode.Single(pair => pair.Value == side).Key;
    }

    public static string ToCode(this TradeSide side)
    {
        return side == TradeSide.Buy ? "buy" : "sell";
    }
}