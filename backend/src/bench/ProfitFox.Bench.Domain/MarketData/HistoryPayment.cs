using System.Globalization;
using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Bench.Domain.MarketData;

/// <summary>
///     A funding payment as the market-data service tells it: its moment and its rate as decimal text.
/// </summary>
public sealed record HistoryPayment(DateTimeOffset Time, string Rate)
{
    public FundingPayment ToFundingPayment()
    {
        return new FundingPayment(Time, decimal.Parse(Rate, NumberStyles.Float, CultureInfo.InvariantCulture));
    }
}
