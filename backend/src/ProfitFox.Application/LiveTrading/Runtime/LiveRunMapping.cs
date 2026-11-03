using System.Globalization;

using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>
///     Maps the books of a live bot as the database keeps them to the run of its strategy and back: the position and the
///     unfinished orders to a <see cref="RunSnapshot" />, the state of the run to positions, and the number of an order to its id
///     in the run.
/// </summary>
internal static class LiveRunMapping
{
    /// <summary>What the run of the strategy goes on from: the current position, the unfinished orders and the PnL of the closed positions.</summary>
    public static RunSnapshot Snapshot(LivePosition? position, decimal pastPnl, IEnumerable<LiveOrder> orders, int lastOrderNumber)
    {
        return new RunSnapshot(
            position?.Number ?? 1,
            position?.Quantity ?? 0m,
            position?.Invested ?? 0m,
            position?.Collateral ?? 0m,
            pastPnl,
            position?.RealizedPnl ?? 0m,
            position?.Fees ?? 0m,
            position?.LastEntryPrice,
            position?.LastEntryAt,
            position?.Closing ?? false,
            position?.SkippedOrders ?? 0,
            [
                .. orders.Select(order => new RunOrder(
                    RunId(order.Number),
                    order.NodePath,
                    order.Side,
                    order.Price,
                    order.Type == OrderType.Limit ? order.Quantity - order.FilledQuantity : null,
                    order.Cancelling,
                    order.Reserve,
                    order.FilledQuantity > 0m))
            ],
            lastOrderNumber);
    }

    /// <summary>The position <paramref name="before" /> as the run closed it with the last event; <c>null</c> when the run closed no position of it.</summary>
    public static LivePosition? Closed(LivePosition? before, RunState state)
    {
        if (state.Closed is not { } closed || before is not { ClosedAt: null } || before.Number != closed.Number)
        {
            return null;
        }

        return before with { Open = false, Closing = false, Quantity = 0m, Invested = 0m, Collateral = 0m, RealizedPnl = closed.RealizedPnl, Fees = closed.Fees, ClosedAt = closed.At };
    }

    /// <summary>The current position of the run.</summary>
    public static LivePosition Current(Guid botId, RunSnapshot snapshot, RunState state)
    {
        return new LivePosition(
            botId,
            snapshot.Position,
            state.Status == PositionStatus.Open,
            snapshot.Closing,
            snapshot.Quantity,
            snapshot.Invested,
            snapshot.Collateral,
            snapshot.RealizedPnl,
            snapshot.Fees,
            snapshot.LastEntryPrice,
            snapshot.LastEntryAt,
            snapshot.SkippedOrders,
            null);
    }

    /// <summary>The id of the order of <paramref name="number" /> in the run.</summary>
    public static string RunId(int number)
    {
        return string.Create(CultureInfo.InvariantCulture, $"o-{number}");
    }

    /// <summary>The number of the order of <paramref name="runId" />.</summary>
    public static int Number(string runId)
    {
        return int.Parse(runId.AsSpan(2), CultureInfo.InvariantCulture);
    }
}