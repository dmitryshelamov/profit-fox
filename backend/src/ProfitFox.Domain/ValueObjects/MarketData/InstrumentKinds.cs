using ProfitFox.Domain.ValueObjects.MarketData.Enums;

namespace ProfitFox.Domain.ValueObjects.MarketData;

/// <summary>
///     The rule of the kind of a pair (change add-instrument-kind-filter, design.md, decision 1).
/// </summary>
public static class InstrumentKinds
{
    // The Bybit types of a share, a fund, a commodity and a currency, as the market-data service gives them; Bybit writes
    // «ETF» where its SDK writes «Etf», so the case does not matter.
    private static readonly (InstrumentKind Kind, string Name)[] Names = [(InstrumentKind.Crypto, "crypto"), (InstrumentKind.Stock, "stock")];

    private static readonly HashSet<string> StockTypes = new(["Stock", "Etf", "Commodity", "Forex", "XStocks"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The names of the kinds as the API and the database write them, as a refusal lists them: «crypto, stock» (decision 4).
    /// </summary>
    public static string Listed { get; } = string.Join(", ", Names.Select(known => known.Name));

    /// <summary>
    ///     The kind of the name <paramref name="name" />, written exactly: <c>crypto</c> or <c>stock</c>.
    /// </summary>
    public static bool TryParse(string? name, out InstrumentKind kind)
    {
        foreach ((InstrumentKind known, string text) in Names)
        {
            if (text == name)
            {
                kind = known;
                return true;
            }
        }

        kind = default;
        return false;
    }

    public static string Name(InstrumentKind kind)
    {
        foreach ((InstrumentKind known, string text) in Names)
        {
            if (known == kind)
            {
                return text;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a kind of a pair");
    }

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
