namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The difference from random entries: the mean of the weekly means as a fraction, the number of weeks and its t, null
///     with fewer than two weeks. A hint, not a verdict.
/// </summary>
public sealed record RandomDifferenceResponse(double Mean, int Weeks, double? T);
