using System.Globalization;

namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>A funding settlement: its time and rate, a fraction such as <c>0.0001</c>, as a decimal string.</summary>
public sealed record FundingRateResponse(DateTimeOffset Time, string Rate)
{
    public static FundingRateResponse Of((DateTimeOffset Time, decimal Value) rate)
    {
        return new FundingRateResponse(rate.Time, rate.Value.ToString(CultureInfo.InvariantCulture));
    }
}