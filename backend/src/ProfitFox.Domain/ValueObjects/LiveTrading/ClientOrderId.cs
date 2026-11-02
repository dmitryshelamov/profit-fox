using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>
///     The id an order of a bot carries to the exchange: the bot id in 25 base-36 digits, a dash and the number of the order in the
///     bot, at most 36 characters as Bybit takes. Numbers go on across the starts of a bot, so a code is never used twice.
/// </summary>
public sealed record ClientOrderId(Guid BotId, int Number)
{
    private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const int BotDigits = 25;

    public string Code => $"{Encode(BotId)}-{Number}";

    public static bool TryParse(string? code, [NotNullWhen(true)] out ClientOrderId? id)
    {
        id = null;
        int dash = code?.IndexOf('-') ?? -1;

        if (dash != BotDigits
            || !int.TryParse(code![(dash + 1)..], System.Globalization.NumberStyles.None, null, out int number)
            || number < 1
            || Decode(code[..dash]) is not { } bot)
        {
            return false;
        }

        id = new ClientOrderId(bot, number);

        return true;
    }

    private static string Encode(Guid id)
    {
        var value = new BigInteger(id.ToByteArray(), isUnsigned: true);
        var digits = new char[BotDigits];

        for (int index = BotDigits - 1; index >= 0; index--)
        {
            digits[index] = Digits[(int)(value % 36)];
            value /= 36;
        }

        return new string(digits);
    }

    private static Guid? Decode(string digits)
    {
        BigInteger value = BigInteger.Zero;

        foreach (char digit in digits)
        {
            int position = Digits.IndexOf(digit, StringComparison.Ordinal);

            if (position < 0)
            {
                return null;
            }

            value = value * 36 + position;
        }

        byte[] bytes = value.ToByteArray(isUnsigned: true);

        return bytes.Length > 16 ? null : new Guid([.. bytes, .. new byte[16 - bytes.Length]]);
    }
}