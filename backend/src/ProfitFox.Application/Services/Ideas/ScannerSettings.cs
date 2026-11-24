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
    ///     How long after its exit an idea whose exit is at the time of a payment of its pair waits for that payment at most
    ///     (task 27.2): a backtest counts it, and the data service gives it a minute or three after its time. Past the limit
    ///     the idea is closed without the payment and the journal says so.
    /// </summary>
    public TimeSpan ExitPaymentWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     How often the scanner looks at its clock when the feed is quiet.
    /// </summary>
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How many events of the feed wait for the scanner at most (task 18.12): a full queue stops the reading of the feed
    ///     until the scanner takes them, and the service that cuts the feed off for it is answered by a new subscription
    ///     from the last item.
    /// </summary>
    public int QueueCapacity { get; init; } = 20_000;

    /// <summary>
    ///     How long the feed may tell no closed minute before ideas no longer count without a break (task 24.1): the newest
    ///     minute of the book older than this beyond its close clears <see cref="IdeaScanner.LiveSince" />. The same setting
    ///     breaks a silent stream of minutes off. Zero or less switches the watch off for both (task 25.2).
    /// </summary>
    public TimeSpan FeedSilence { get; init; } = TimeSpan.FromMinutes(3);
}
