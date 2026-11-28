namespace ProfitFox.Domain.Entities.Trades.Enums;

/// <summary>
///     What an order of the bench is for (design.md, decision 4): the entry of a trade or its close.
/// </summary>
public enum TradeOrderPurpose
{
    Entry,
    Close
}
