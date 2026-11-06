using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>An entry to check: the idea with its levels, the price the trade enters at, the price the market is at now, the size it was given and the rules of the instrument.</summary>
public sealed record IdeaTradeEntry(MarketCategory Category, IdeaStatus IdeaStatus, IdeaPlan Plan, decimal Price, decimal Current, IdeaTradeSize Size, InstrumentRules Rules);