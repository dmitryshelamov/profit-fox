using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>
///     An order a node placed, or the closing position itself when <see cref="Node" /> is <c>null</c>, that is not finished yet; <see cref="Remaining" /> is the unfilled quantity of a limit order,
///     <see cref="Reserve" /> the money an unfilled market entry holds back: its value at the time it was placed, with the fee. <see cref="Filled" /> tells an order that has a fill already.
/// </summary>
internal sealed record ActiveOrder(int? Node, OrderSide Side, decimal? Price = null, decimal? Remaining = null, bool Cancelling = false, decimal Reserve = 0m, bool Filled = false);