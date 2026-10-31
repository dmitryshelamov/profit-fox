namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>Candle price exactly as the exchange reported it; no invariants on purpose.</summary>
public readonly record struct Price(decimal Value);
