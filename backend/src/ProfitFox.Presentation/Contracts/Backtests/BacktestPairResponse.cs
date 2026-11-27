namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The numbers of a set on one pair.
/// </summary>
public sealed record BacktestPairResponse(string Pair, SetNumbersResponse Numbers);
