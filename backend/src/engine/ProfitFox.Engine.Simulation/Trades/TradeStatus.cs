namespace ProfitFox.Engine.Simulation.Trades;

/// <summary>
///     Whether a trade counts, or why it is left out.
/// </summary>
public enum TradeStatus
{
    Ok,

    /// <summary>
    ///     The horizon leaves the coverage of minute candles.
    /// </summary>
    Coverage,

    /// <summary>
    ///     No minute of the horizon had trades.
    /// </summary>
    NoTrade,

    /// <summary>
    ///     The horizon leaves the coverage of funding payments.
    /// </summary>
    Funding,

    /// <summary>
    ///     The entry order gave no trade plan: its time, stop or target is missing or not allowed (spec strategy-position
    ///     «Недопустимая заявка»).
    /// </summary>
    Plan
}
