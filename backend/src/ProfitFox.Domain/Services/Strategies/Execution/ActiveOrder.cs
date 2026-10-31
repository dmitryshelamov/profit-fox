using ProfitFox.Domain.Entities.Strategies.Enums;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>An order a node placed that is not finished yet; <see cref="Remaining" /> is the unfilled quantity of a limit order.</summary>
internal sealed record ActiveOrder(int Node, OrderSide Side, decimal? Price = null, decimal? Remaining = null, bool Cancelling = false);