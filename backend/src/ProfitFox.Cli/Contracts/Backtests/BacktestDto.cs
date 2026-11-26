namespace ProfitFox.Cli.Contracts.Backtests;

/// <summary>
///     A backtest as the bench API tells it: only the fields the commands print.
/// </summary>
internal sealed record BacktestDto(
    string Id,
    BacktestStrategyDto Strategy,
    BacktestSettingsDto Settings,
    string State,
    string? Worker,
    int PairsDone,
    int? PairsTotal,
    int SetCount,
    IReadOnlyList<BacktestSetDto>? Sets,
    string? Reason,
    double? BestAmount);

/// <summary>
///     The strategy of a backtest: its name, version and kind.
/// </summary>
internal sealed record BacktestStrategyDto(string Name, int Version, string Kind);

/// <summary>
///     The history of a backtest and its timeframes.
/// </summary>
internal sealed record BacktestSettingsDto(string Start, string End, IReadOnlyList<string> Timeframes);

/// <summary>
///     A set of a backtest with its numbers once it is done, and the mark of a set of a category of ideas of engine version 5
///     and later.
/// </summary>
internal sealed record BacktestSetDto(int Number, string Timeframe, IReadOnlyList<ParameterValueDto> Values, SetNumbersDto? Numbers, SetMarkDto? Mark = null);

/// <summary>
///     The value of a parameter in a set.
/// </summary>
internal sealed record ParameterValueDto(string Name, string Value);

/// <summary>
///     The numbers of a set the table of <c>bench run --wait</c> shows, with the numbers by weeks of a set of a category of
///     ideas.
/// </summary>
internal sealed record SetNumbersDto(int Trades, double? Mean, double Amount, double? WinShare, int OpenCycles, RandomDifferenceDto? Difference, WeeklyNumbersDto? Weekly = null);

/// <summary>
///     The difference from random entries of a set of a category of ideas.
/// </summary>
internal sealed record RandomDifferenceDto(double Mean, int Weeks, double? T);

/// <summary>
///     The numbers of a set of a category of ideas by weeks: the weeks with trades, the mean of the weekly means, the weeks
///     up, the number of the best weeks and the sum in USDT without them.
/// </summary>
internal sealed record WeeklyNumbersDto(int Weeks, double Mean, int WeeksUp, int BestWeeks, double? WithoutBest);

/// <summary>
///     The mark of a set: <c>candidate</c>, <c>passed</c>, <c>failed</c>, <c>unproven</c> or none, and which of the five
///     conditions the set met.
/// </summary>
internal sealed record SetMarkDto(string? Value, MarkConditionsDto Conditions);

/// <summary>
///     Which of the five conditions of the mark a set met.
/// </summary>
internal sealed record MarkConditionsDto(bool Weeks, bool WeeklyMean, bool WeeksUp, bool WithoutBest, bool Difference);