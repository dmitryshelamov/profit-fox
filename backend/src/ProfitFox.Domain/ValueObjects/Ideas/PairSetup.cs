using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>A setup that holds for a pair on an interval on the last closed minute.</summary>
public sealed record PairSetup(Interval Interval, IdeaSetup Setup);