namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The values a backtest gave a parameter, as texts.
/// </summary>
public sealed record ParameterValuesResponse(string Name, IReadOnlyList<string> Values);
