using System.Numerics;

namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     The number of an order of the bench at Bybit, its <c>orderLinkId</c> (design.md, decision 4):
///     <c>t{the id of the trade in base 36, 25 characters}-{the number of the order}</c>, at most 36 characters. It comes
///     from the row of the order, so a repeat of the send carries the same number, and it reads back to the trade and the
///     order.
/// </summary>
public static class TradeOrderId
{
    private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const int Width = 25;
    private const int MaxLength = 36;

    public static string Format(Guid tradeId, int number)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "The number of an order starts at 1.");
        }

        var value = new BigInteger(tradeId.ToByteArray(), isUnsigned: true);
        var text = new char[Width];
        for (int index = Width - 1; index >= 0; index--)
        {
            text[index] = Digits[(int)(value % 36)];
            value /= 36;
        }

        string id = $"t{new string(text)}-{number}";
        return id.Length <= MaxLength ? id : throw new ArgumentOutOfRangeException(nameof(number), number, "The number of the order makes the id too long.");
    }

    /// <summary>
    ///     The trade and the number of an order of the bench; null for a string that is not one.
    /// </summary>
    public static (Guid TradeId, int Number)? Parse(string? linkId)
    {
        if (linkId is null || linkId.Length < Width + 3 || linkId.Length > MaxLength || linkId[0] != 't' || linkId[Width + 1] != '-')
        {
            return null;
        }

        BigInteger value = BigInteger.Zero;
        for (int index = 1; index <= Width; index++)
        {
            int digit = Digits.IndexOf(linkId[index], StringComparison.Ordinal);
            if (digit < 0)
            {
                return null;
            }

            value = value * 36 + digit;
        }

        string tail = linkId[(Width + 2)..];
        if (tail.Length == 0 || tail[0] == '0' || !tail.All(char.IsAsciiDigit) || !int.TryParse(tail, out int number))
        {
            return null;
        }

        byte[] bytes = value.ToByteArray(isUnsigned: true);
        if (bytes.Length > 16)
        {
            return null;
        }

        var full = new byte[16];
        bytes.CopyTo(full, 0);
        return (new Guid(full), number);
    }
}
