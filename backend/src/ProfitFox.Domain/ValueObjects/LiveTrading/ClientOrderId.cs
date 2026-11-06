using System.Diagnostics.CodeAnalysis;

namespace ProfitFox.Domain.ValueObjects.LiveTrading;

/// <summary>
///     The id an order of a bot carries to the exchange: the bot id in 25 base-36 digits, a dash and the number of the order in the
///     bot, at most 36 characters as Bybit takes. Numbers go on across the starts of a bot, so a code is never used twice.
/// </summary>
public sealed record ClientOrderId(Guid BotId, int Number)
{
    public string Code => $"{Base36Guid.Encode(BotId)}-{Number}";

    public static bool TryParse(string? code, [NotNullWhen(true)] out ClientOrderId? id)
    {
        id = null;
        int dash = code?.IndexOf('-') ?? -1;

        if (dash != Base36Guid.Length
            || !int.TryParse(code![(dash + 1)..], System.Globalization.NumberStyles.None, null, out int number)
            || number < 1
            || Base36Guid.Decode(code[..dash]) is not { } bot)
        {
            return false;
        }

        id = new ClientOrderId(bot, number);

        return true;
    }
}