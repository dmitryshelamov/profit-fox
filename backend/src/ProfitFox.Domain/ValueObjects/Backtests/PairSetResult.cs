using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What a set gave on a pair: its numbers, and what they are made of — the rows of the orders of a category of ideas
///     with their random entries, or the cycles and orders of a strategy for a bot.
/// </summary>
public sealed record PairSetResult(int Set, SetNumbers Numbers, IReadOnlyList<EventRow> Rows, PositionResult? Cycles);
