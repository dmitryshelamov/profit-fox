using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

namespace ProfitFox.Application.ExchangeAccounts;

/// <summary>Codes of the exchanges in the API and in stored rows: <c>bybit</c>, <c>tinvest</c>.</summary>
public static class ExchangeCodes
{
    private static readonly Dictionary<string, Exchange> ByCode = new Dictionary<string, Exchange>
    {
        ["bybit"] = Exchange.Bybit,
        ["tinvest"] = Exchange.TInvest
    };

    private static readonly Dictionary<Exchange, string> Codes = ByCode.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static bool TryParse(string code, out Exchange exchange)
    {
        return ByCode.TryGetValue(code, out exchange);
    }

    public static string ToCode(this Exchange exchange)
    {
        return Codes[exchange];
    }
}