using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>An instrument the exchange lists in a market category, with the coins it trades.</summary>
public sealed record ListedInstrument(MarketCategory Category, string Symbol, string BaseCoin, string QuoteCoin);