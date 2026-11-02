using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>
///     An active order of a run snapshot. <see cref="NodePath" /> is the node that placed it, <c>null</c> for the market exit of a
///     closing position; <see cref="Remaining" /> is the unfilled quantity of a limit order, <see cref="Reserve" /> what an unfilled
///     market entry holds back.
/// </summary>
public sealed record RunOrder(
    string Id,
    string? NodePath,
    OrderSide Side,
    decimal? Price,
    decimal? Remaining,
    bool Cancelling = false,
    decimal Reserve = 0m,
    bool Filled = false);