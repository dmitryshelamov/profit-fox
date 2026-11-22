namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     A set of a backtest: its number, timeframe and values, and its numbers over every pair once the backtest is done.
/// </summary>
public sealed record BacktestSetResponse(int Number, string Timeframe, IReadOnlyList<ParameterValueResponse> Values, SetNumbersResponse? Numbers);
