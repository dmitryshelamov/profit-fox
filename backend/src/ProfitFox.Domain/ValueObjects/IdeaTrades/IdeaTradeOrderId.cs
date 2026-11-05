using System.Diagnostics.CodeAnalysis;

using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>
///     The id an order of an idea trade carries to the exchange: <c>i</c>, the trade id in 25 base-36 digits, a dash and the number
///     of the order in the trade, at most 36 characters as Bybit takes. The dash stands one place later than in the id of a bot's
///     order, so neither code reads as the other. Numbers never repeat, so a resent order is the same order.
/// </summary>
public sealed record IdeaTradeOrderId(Guid TradeId, int Number)
{
    /// <summary>The largest number whose code still fits the 36 characters of Bybit; a trade has a handful of orders.</summary>
    public const int MaxNumber = 999_999_999;

    private const string Prefix = "i";

    public string Code => $"{Prefix}{Base36Guid.Encode(TradeId)}-{Number}";

    public static bool TryParse(string? code, [NotNullWhen(true)] out IdeaTradeOrderId? id)
    {
        id = null;
        int dash = code?.IndexOf('-') ?? -1;

        if (dash != Prefix.Length + Base36Guid.Length
            || !code!.StartsWith(Prefix, StringComparison.Ordinal)
            || !int.TryParse(code[(dash + 1)..], System.Globalization.NumberStyles.None, null, out int number)
            || number is < 1 or > MaxNumber
            || Base36Guid.Decode(code[Prefix.Length..dash]) is not { } trade)
        {
            return false;
        }

        id = new IdeaTradeOrderId(trade, number);

        return true;
    }
}