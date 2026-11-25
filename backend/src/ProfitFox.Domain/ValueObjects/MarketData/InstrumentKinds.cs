using ProfitFox.Domain.ValueObjects.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     The rule of the kind of a pair (change add-instrument-kind-filter, design.md, decision 1).
/// </summary>
public static class InstrumentKinds
{
    // The Bybit types of a share, a fund, a commodity and a currency, as the market-data service gives them; Bybit writes
    // «ETF» where its SDK writes «Etf», so the case does not matter.
    private static readonly HashSet<string> StockTypes = new(["Stock", "Etf", "Commodity", "Forex", "XStocks"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The kind of a pair of the Bybit type <paramref name="symbolType" />: a stock for a share, a fund, a commodity or a
    ///     currency, crypto for any other type and without one. An unknown type is crypto: a pair too many in crypto is
    ///     better than a pair lost.
    /// </summary>
    public static InstrumentKind Of(string? symbolType)
    {
        return symbolType is not null && StockTypes.Contains(symbolType) ? InstrumentKind.Stock : InstrumentKind.Crypto;
    }
}
