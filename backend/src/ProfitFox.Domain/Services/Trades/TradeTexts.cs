using System.Globalization;

namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     Numbers as the reasons of the risk module write them: whole sums with a space between thousands (<c>1 000</c>), others
///     with two digits after the point (<c>20.14</c>), and minutes in words.
/// </summary>
public static class TradeTexts
{
    private static readonly NumberFormatInfo Format = new NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalSeparator = "." };

    public static string Money(decimal value)
    {
        return value == decimal.Truncate(value) ? value.ToString("#,0", Format) : value.ToString("#,0.00", Format);
    }

    /// <summary>
    ///     A price as Bybit writes it, without the noughts at the end.
    /// </summary>
    public static string Price(decimal value)
    {
        return value.ToString("0.############", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     «меньше минуты», «меньше 5 минут», «меньше 21 минуты».
    /// </summary>
    public static string LessThanMinutes(int minutes)
    {
        if (minutes == 1)
        {
            return "меньше минуты";
        }

        return minutes % 10 == 1 && minutes % 100 != 11 ? $"меньше {minutes} минуты" : $"меньше {minutes} минут";
    }
}
