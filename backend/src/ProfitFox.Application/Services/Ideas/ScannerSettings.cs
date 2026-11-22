namespace ProfitFox.Application.Services.Ideas;

/// <summary>
///     The <c>Scanner</c> section of the settings of the Worker (change add-ideas-and-market, design.md, decisions 1, 3, 4
///     and 5): whether this Worker may keep the live data, how long after its close a candle counts as closed, how much
///     history warms a graph up and how many pairs at once, how often the enabled strategies are looked at, how long a
///     closed idea waits for the payment at its exit, and how soon a pair whose history is loading is asked again.
/// </summary>
public sealed class ScannerSettings
{
    public bool Enabled { get; init; } = true;

    public TimeSpan CloseDelay { get; init; } = TimeSpan.FromSeconds(15);

    public int WarmupCandles { get; init; } = 200;

    public int WarmupParallelism { get; init; } = 4;

    public TimeSpan WarmupRetry { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan ReconcileInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ExitWait { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    ///     How often the scanner looks at its clock when the feed is quiet.
    /// </summary>
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(1);
}
