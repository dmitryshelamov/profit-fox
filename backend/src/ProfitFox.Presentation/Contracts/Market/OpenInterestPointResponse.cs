using System.Globalization;

namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>The open interest at the start of a 5 minute step in USDT, at the close of the minute before it, as a decimal string.</summary>
public sealed record OpenInterestPointResponse(DateTimeOffset Time, string Value)
{
    public static OpenInterestPointResponse Of((DateTimeOffset Time, decimal Value) point)
    {
        return new OpenInterestPointResponse(point.Time, point.Value.ToString(CultureInfo.InvariantCulture));
    }
}