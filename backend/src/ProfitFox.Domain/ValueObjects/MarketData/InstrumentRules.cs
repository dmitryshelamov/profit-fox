namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>What the exchange accepts for an instrument: prices on the tick, quantities on the step, and no order below the minimums.</summary>
/// <param name="TickSize">Step of the price.</param>
/// <param name="QuantityStep">Step of the quantity of the base asset.</param>
/// <param name="MinQuantity">Smallest quantity of an order.</param>
/// <param name="MinAmount">Smallest value of an order in the quote asset.</param>
public sealed record InstrumentRules(decimal TickSize, decimal QuantityStep, decimal MinQuantity, decimal MinAmount);