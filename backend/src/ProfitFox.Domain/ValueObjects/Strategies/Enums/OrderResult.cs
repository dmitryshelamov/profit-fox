namespace ProfitFox.Domain.ValueObjects.Strategies.Enums;

/// <summary>
///     What became of an entry order of a strategy in its preview (spec strategy-preview «Исход заявки»).
/// </summary>
public enum OrderResult
{
    /// <summary>
    ///     The order opened a trade.
    /// </summary>
    Opened,

    /// <summary>
    ///     The order came while the position of the pair was open.
    /// </summary>
    Skipped,

    /// <summary>
    ///     The order gave no trade; <see cref="OrderExclusion" /> says why.
    /// </summary>
    Excluded,

    /// <summary>
    ///     The order is in a week before those the trades are computed for.
    /// </summary>
    NotComputed
}
