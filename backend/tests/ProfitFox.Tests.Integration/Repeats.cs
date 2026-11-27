using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;

namespace ProfitFox.Tests.Integration;

/// <summary>
///     The repeats of a dropped read in the tests: as many as the bench makes, without a pause, so that no test waits.
/// </summary>
internal static class Repeats
{
    public static readonly HistoryRepeats WithoutPauses = new HistoryRepeats { Pauses = [.. HistoryRepeats.DefaultPauses.Select(_ => TimeSpan.Zero)] };
}
