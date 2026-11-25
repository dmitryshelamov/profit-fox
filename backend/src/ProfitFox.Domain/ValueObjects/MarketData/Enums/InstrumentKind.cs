namespace ProfitFox.Domain.ValueObjects.MarketData.Enums;

/// <summary>
///     The kind of a pair of the set of pairs: crypto, or a contract on a share, a fund, a commodity or a currency (spec
///     market-overview «Вид пары»).
/// </summary>
public enum InstrumentKind
{
    Crypto,
    Stock
}
