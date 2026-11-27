namespace ProfitFox.Domain.ValueObjects.Strategies.Enums;

/// <summary>
///     What became of an order of a strategy in its preview (spec strategy-preview «Исход заявки»).
/// </summary>
public enum OrderResult
{
    /// <summary>
    ///     The order filled: an order of <c>enter</c> opened a cycle, an order of another node filled in one.
    /// </summary>
    Filled,

    /// <summary>
    ///     The name of <see cref="Filled" /> before change add-position-node, when every order was an entry that opened a
    ///     trade; the same value.
    /// </summary>
    Opened = Filled,

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
