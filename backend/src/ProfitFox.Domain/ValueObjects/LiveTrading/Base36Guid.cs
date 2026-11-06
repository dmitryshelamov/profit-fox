using System.Numerics;

namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>A guid as 25 base-36 digits, the way the ids of orders carry it to the exchange.</summary>
internal static class Base36Guid
{
    internal const int Length = 25;

    private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

    internal static string Encode(Guid id)
    {
        var value = new BigInteger(id.ToByteArray(), isUnsigned: true);
        var digits = new char[Length];

        for (int index = Length - 1; index >= 0; index--)
        {
            digits[index] = Digits[(int)(value % 36)];
            value /= 36;
        }

        return new string(digits);
    }

    internal static Guid? Decode(string digits)
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