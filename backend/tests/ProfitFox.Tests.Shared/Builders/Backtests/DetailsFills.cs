using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Tests.Shared.Builders.Backtests;

public static class DetailsFills
{
    /// <summary>Every fill of the details with its order, in the order of time and then of the positions and their orders.</summary>
    public static IReadOnlyList<(BacktestOrder Order, BacktestFill Fill)> AllFills(this BacktestDetails details)
    {
        return
        [
            .. details.Positions
                .SelectMany(position => position.Orders)
                .SelectMany(order => order.Fills.Select(fill => (order, fill)))
                .OrderBy(entry => entry.fill.At)
        ];
    }
}