namespace ProfitFox.Domain.Entities.Backtests;

/// <summary>
///     A backtest of a saved version of a strategy (spec strategy-backtest; change add-strategy-backtest, design.md,
///     decision 2): what it computes and the limits of what it may be asked.
/// </summary>
public sealed class Backtest
{
    /// <summary>
    ///     The most sets a backtest computes (design.md, decision 3).
    /// </summary>
    public const int MaxSets = 200;

    /// <summary>
    ///     The most pairs a backtest of a strategy for a bot takes (design.md, decision 4): its minutes are read for the whole
    ///     history.
    /// </summary>
    public const int MaxBotPairs = 20;

    /// <summary>
    ///     How many weeks before the end the history of a strategy for a bot starts when the request has no start.
    /// </summary>
    public const int BotWeeks = 26;

    /// <summary>
    ///     The random entries of each order of a category of ideas.
    /// </summary>
    public const int Randoms = 20;
}
