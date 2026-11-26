namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     What a set gave on a pair in one week on its minute path: the rows of its own orders and the random entries the pair
///     took for the orders of the other pairs of the week.
/// </summary>
public sealed record PairWeek(IReadOnlyList<EventRow> Rows, IReadOnlyList<RandomTrade> Randoms);