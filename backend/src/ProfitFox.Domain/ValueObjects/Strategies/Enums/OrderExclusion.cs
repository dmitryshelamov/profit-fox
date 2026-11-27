namespace ProfitFox.Domain.ValueObjects.Strategies.Enums;

/// <summary>
///     Why an entry order gave no trade: the status of the calculation of its cycle, or <see cref="Period" /> for a trade
///     that would end after the end of the history the preview may read.
/// </summary>
public enum OrderExclusion
{
    Plan,
    Coverage,
    NoTrade,
    Funding,
    Period
}
