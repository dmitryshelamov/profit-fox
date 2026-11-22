namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The numbers of a set on one pair.
/// </summary>
public sealed record PairSummary(string Pair, SetSummary Numbers);
