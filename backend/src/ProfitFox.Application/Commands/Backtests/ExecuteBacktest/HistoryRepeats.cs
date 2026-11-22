namespace ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     How a backtest repeats a read the market-data service dropped (task 12.12): one repeat after each pause, the pauses
///     growing. The previews do not repeat: on a dead service they answer at once.
/// </summary>
public sealed class HistoryRepeats
{
    /// <summary>
    ///     Three repeats, after 5, 15 and 45 seconds: a little over a minute for the service to come back, while the Worker keeps
    ///     the lease of the backtest.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultPauses = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];

    /// <summary>
    ///     The pause before each repeat, in order; as many repeats as pauses.
    /// </summary>
    public IReadOnlyList<TimeSpan> Pauses { get; init; } = DefaultPauses;
}
