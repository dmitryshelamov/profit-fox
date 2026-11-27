namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     How many pairs of the market and open ideas a set of conditions matches, and how many there are (spec saved-filters
///     «Счётчик подходящих строк»).
/// </summary>
public sealed record FilterCount(int Pairs, int AllPairs, int Ideas, int AllIdeas);
