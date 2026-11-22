namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The settings of a backtest: the values given to the parameters, the timeframes, history from <see cref="Start" />
///     to <see cref="End" />, the end not included, as dates UTC, the pairs (null for every listed pair) and the fees.
/// </summary>
public sealed record BacktestSettingsResponse(
    IReadOnlyList<ParameterValuesResponse> Values,
    IReadOnlyList<string> Timeframes,
    string Start,
    string End,
    IReadOnlyList<string>? Pairs,
    double TakerFee,
    double MakerFee);
