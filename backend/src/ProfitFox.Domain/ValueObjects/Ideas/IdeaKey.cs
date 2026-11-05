using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>What one open idea at a time stands for: a pair, a setup, a type, an interval and a side.</summary>
public sealed record IdeaKey(Instrument Instrument, IdeaSetup Setup, IdeaType Type, Interval Interval, IdeaSide Side);