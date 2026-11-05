using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The bucket a case counts in: setup × type × interval × side × category.</summary>
public sealed record IdeaBucketKey(IdeaSetup Setup, IdeaType Type, Interval Interval, IdeaSide Side, MarketCategory Category);