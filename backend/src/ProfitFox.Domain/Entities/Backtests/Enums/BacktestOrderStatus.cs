namespace ProfitFox.Domain.Entities.Backtests.Enums;

/// <summary>
///     Where an order of a result's details was at the end of the period. The replay fills an order whole, so an order with a fill is never
///     cancelled, and one cancelled without a fill is not in the details.
/// </summary>
public enum BacktestOrderStatus
{
    Filled,
    Active
}