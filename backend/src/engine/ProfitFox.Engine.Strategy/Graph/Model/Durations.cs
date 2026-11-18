using System.Globalization;

namespace ProfitFox.Engine.Strategy.Graph.Model;

/// <summary>
///     Durations as text: a whole positive number with a unit, <c>30m</c>, <c>4h</c>, <c>1d</c> or <c>1w</c>.
/// </summary>
public static class Durations
{
    public static bool TryParse(string text, out TimeSpan duration)
    {
        duration = default;

        if (text.Length < 2)
        {
            return false;
        }

        string digits = text[..^1];

        if (!digits.All(char.IsAsciiDigit)
            || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            || value <= 0)
        {
            return false;
        }

        long minutes = text[^1] switch
        {
            'm' => 1,
            'h' => 60,
            'd' => 1440,
            'w' => 10080,
            _ => 0
        };

        if (minutes == 0 || value > long.MaxValue / minutes / TimeSpan.TicksPerMinute)
        {
            return false;
        }

        duration = TimeSpan.FromMinutes(value * minutes);

        return true;
    }

    /// <summary>
    ///     The text of a whole number of minutes, in the largest unit that divides it.
    /// </summary>
    public static string Format(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "A duration is a whole positive number of minutes.");
        }

        long minutes = duration.Ticks / TimeSpan.TicksPerMinute;

        foreach ((long size, char unit) in new[] { (10080L, 'w'), (1440L, 'd'), (60L, 'h') })
        {
            if (minutes % size == 0)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{minutes / size}{unit}");
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
    }
}