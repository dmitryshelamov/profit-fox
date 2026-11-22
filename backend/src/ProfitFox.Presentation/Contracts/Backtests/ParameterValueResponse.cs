namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The value of a parameter in a set.
/// </summary>
public sealed record ParameterValueResponse(string Name, string Value);
