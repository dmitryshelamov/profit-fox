using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>The rules an engine of ideas runs by: the intervals with their thresholds, k of the stop buffer and the rules of the zones.</summary>
public sealed record IdeaRuleSet(IReadOnlyDictionary<Interval, SetupThresholds> Thresholds, decimal StopBuffer, ZoneRules Zones);