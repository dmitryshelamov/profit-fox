using System.Diagnostics;

namespace ProfitFox.Tests.Integration.Bench.Workers;

/// <summary>
///     Waits for what a Worker does in the background.
/// </summary>
internal static class Poll
{
    /// <summary>
    ///     Asks <paramref name="condition" /> every 50 ms until it is true.
    /// </summary>
    /// <returns>How long it took.</returns>
    public static async Task<TimeSpan> Until(string what, Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var clock = Stopwatch.StartNew();
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(30);
        while (!await condition())
        {
            if (clock.Elapsed > limit)
            {
                Assert.Fail($"Waited {limit.TotalSeconds:F0} s for: {what}");
            }

            await Task.Delay(50);
        }

        return clock.Elapsed;
    }
}
