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
///     A set of a backtest with its numbers once it is done.
/// </summary>
internal sealed record BacktestSetDto(int Number, string Timeframe, IReadOnlyList<ParameterValueDto> Values, SetNumbersDto? Numbers);

/// <summary>
///     The value of a parameter in a set.
/// </summary>
internal sealed record ParameterValueDto(string Name, string Value);

/// <summary>
///     The numbers of a set the table of <c>bench run --wait</c> shows.
/// </summary>
internal sealed record SetNumbersDto(int Trades, double? Mean, double Amount, double? WinShare, int OpenCycles, RandomDifferenceDto? Difference);

/// <summary>
///     The difference from random entries of a set of a category of ideas.
/// </summary>
internal sealed record RandomDifferenceDto(double Mean, int Weeks, double? T);
