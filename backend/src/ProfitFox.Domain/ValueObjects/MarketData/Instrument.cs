using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>A traded contract: the same symbol in different market categories is a different instrument.</summary>
public sealed record Instrument
{
    public Instrument(MarketCategory category, string symbol)
    {
        Category = category;
        Symbol = symbol.ToUpperInvariant();
    }

    public MarketCategory Category { get; }

    public string Symbol { get; }
}
