namespace ProfitFox.Application.Services.Trades;

/// <summary>
///     The section <c>Trading</c> of the settings of the Worker (change take-idea-trades, design.md, decisions 2 and 3): whether
///     this Worker may be the trading one, how often it reads the base, how often it polls an account without a stream and
///     checks one with a stream by REST, how long a stream has to open, how long a stream outlives the last trade of its
///     account, and how long an empty position waits for its closing fills. The wait for the entry is not here: it is a
///     parameter of the risk module. <see cref="MarkInterval" /> is how often the results of the open trades are written.
/// </summary>
public sealed class TradingSettings
{
    public bool Enabled { get; init; }

    public TimeSpan Tick { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan ControlInterval { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan StreamOpenWait { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StreamIdle { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan CloseWait { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How far, as a fraction, the price may go from the price of the click in the way of the trade before its entry is
    ///     sent (review of the application, N1): the stop is counted from the price of the click, so the loss at the stop is
    ///     at most the limit of the loss of a trade plus this. Beyond it the entry is given up.
    /// </summary>
    public decimal EntryDrift { get; init; } = 0.005m;

    public TimeSpan MarkInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     The longest pause between two openings of a stream the package gave up on, and between two sends of an order.
    /// </summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     The greatest <see cref="EntryDrift" />: 5 %.
    /// </summary>
    public const decimal MaxEntryDrift = 0.05m;

    /// <summary>
    ///     What is wrong with the settings (review of the application, R7): every interval above nought, the drift a fraction
    ///     above nought and at most <see cref="MaxEntryDrift" />. Empty when they are right.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        foreach ((string name, TimeSpan value) in new[]
                 {
                     (nameof(Tick), Tick), (nameof(PollInterval), PollInterval), (nameof(ControlInterval), ControlInterval),
                     (nameof(StreamOpenWait), StreamOpenWait), (nameof(StreamIdle), StreamIdle), (nameof(CloseWait), CloseWait),
                     (nameof(MarkInterval), MarkInterval), (nameof(MaxBackoff), MaxBackoff)
                 })
        {
            if (value <= TimeSpan.Zero)
            {
                problems.Add($"Trading:{name} must be above nought, it is {value}.");
            }
        }

        if (EntryDrift <= 0 || EntryDrift > MaxEntryDrift)
        {
            problems.Add($"Trading:EntryDrift is a fraction above 0 and at most {MaxEntryDrift} (0.005 is 0.5 %), not percent; it is {EntryDrift}.");
        }

        return problems;
    }
}
