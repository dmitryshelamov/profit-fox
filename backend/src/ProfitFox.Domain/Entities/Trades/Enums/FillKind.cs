namespace ProfitFox.Domain.Entities.Trades.Enums;

/// <summary>
///     What a fill of the pair of a trade is to it (design.md, decision 5): a fill of its entry order, a payment of funding,
///     a fill that makes the position smaller — its reason tells which —, or a fill that makes the position bigger and is not
///     the entry of the bench.
/// </summary>
public enum FillKind
{
    Entry,
    Funding,
    Exit,
    Increase
}
