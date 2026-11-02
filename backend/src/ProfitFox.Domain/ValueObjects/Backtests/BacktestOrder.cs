using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>An order of a position in a result's details, with its fills.</summary>
/// <param name="Id"></param>
/// <param name="Node">The path of the node that placed it; <c>null</c> when the closing position placed it.</param>
/// <param name="Side"></param>
/// <param name="Type"></param>
/// <param name="Price">The limit price; <c>null</c> for a market order.</param>
/// <param name="Quantity"></param>
/// <param name="Amount">The amount of a market order sized by amount.</param>
/// <param name="Status"></param>
/// <param name="Fills"></param>
public sealed record BacktestOrder(
    string Id,
    string? Node,
    OrderSide Side,
    OrderType Type,
    decimal? Price,
    decimal? Quantity,
    decimal? Amount,
    BacktestOrderStatus Status,
    IReadOnlyList<BacktestFill> Fills);