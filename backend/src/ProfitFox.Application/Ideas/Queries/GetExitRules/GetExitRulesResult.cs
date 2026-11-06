using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetExitRules;

public sealed record GetExitRulesResult(IReadOnlyList<IdeaExitRule> Rules, IReadOnlyDictionary<Interval, IReadOnlyList<int>> Grids);