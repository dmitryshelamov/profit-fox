namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A page of the trades (change take-idea-trades, design.md, decision 13): the open ones — every state that holds its pair —
///     or the closed and cancelled ones, sorted by a field of <see cref="Services.Lists.ListFields.Trades" />.
/// </summary>
public sealed record TradeListQuery(bool Open, ListOrder Order);
