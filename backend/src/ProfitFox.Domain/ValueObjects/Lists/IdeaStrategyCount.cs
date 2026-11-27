namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     How many ideas of a list a strategy has, by the name of the strategy (change add-setup-strategies, design.md, decision
///     9): the name it has now, or the one its ideas were born with when it is gone.
/// </summary>
public sealed record IdeaStrategyCount(string Strategy, int Ideas);
