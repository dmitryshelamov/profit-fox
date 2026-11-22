namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The values a backtest gives one parameter of the strategy, as texts, in the order of the request.
/// </summary>
public sealed record ParameterValues(string Name, IReadOnlyList<string> Values);
