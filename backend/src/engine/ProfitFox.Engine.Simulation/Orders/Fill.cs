namespace ProfitFox.Engine.Simulation.Orders;

/// <summary>
///     The fill of a whole order: when, at what price after slippage, at what open of its minute before it, the quantity and the fee in
///     the quote asset.
/// </summary>
public sealed record Fill(DateTimeOffset Time, decimal Price, decimal MarketPrice, decimal Quantity, decimal Fee);