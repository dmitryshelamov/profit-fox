namespace ProfitFox.Engine.Simulation.Plans;

/// <summary>
///     Whether a trade has an outcome, and if not, why.
/// </summary>
public enum TradeStatus
{
    /// <summary>
    ///     The trade has an outcome.
    /// </summary>
    Ok,

    /// <summary>
    ///     A minute from the entry moment to the exit fill is not covered, or the exit did not fill.
    /// </summary>
    ExcludedCoverage,

    /// <summary>
    ///     The pair had no trade from the entry moment to the end of the horizon.
    /// </summary>
    ExcludedNoTrade,

    /// <summary>
    ///     The funding payments are not known from the entry moment to the exit fill.
    /// </summary>
    ExcludedFunding
}