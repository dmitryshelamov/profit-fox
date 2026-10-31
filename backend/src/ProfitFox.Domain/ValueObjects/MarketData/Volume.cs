namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>Traded amount (base volume or quote turnover) exactly as the exchange reported it; no invariants on purpose.</summary>
public readonly record struct Volume(decimal Value);
