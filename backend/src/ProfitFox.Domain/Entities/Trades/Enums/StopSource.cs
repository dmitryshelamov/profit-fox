namespace ProfitFox.Domain.Entities.Trades.Enums;

/// <summary>
///     Where the stop of a trade comes from (spec trade-risk «Защитный стоп»): the stop of its idea, or the price at the limit
///     of the loss of a trade.
/// </summary>
public enum StopSource
{
    Idea,
    Limit
}
