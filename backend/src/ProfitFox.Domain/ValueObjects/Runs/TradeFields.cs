using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     The result of a trade or a mean of several (<see cref="Y" />, the result after costs) and its parts, as fractions of
///     the trade amount.
/// </summary>
public sealed record TradeFields(double Y, double Gross, double Fees, double Slippage, double Funding, double Net)
{
    public static TradeFields Of(TradeOutcome outcome)
    {
        return new TradeFields(
            outcome.Net!.Value,
            outcome.Gross!.Value,
            outcome.Fees!.Value,
            outcome.Slippage!.Value,
            outcome.Funding!.Value,
            outcome.Net!.Value);
    }

    public static TradeFields? Mean(IReadOnlyList<TradeFields> trades)
    {
        return trades.Count == 0
            ? null
            : new TradeFields(
                trades.Average(trade => trade.Y),
                trades.Average(trade => trade.Gross),
                trades.Average(trade => trade.Fees),
                trades.Average(trade => trade.Slippage),
                trades.Average(trade => trade.Funding),
                trades.Average(trade => trade.Net));
    }
}
