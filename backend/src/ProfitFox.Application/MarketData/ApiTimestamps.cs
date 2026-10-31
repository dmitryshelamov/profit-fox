using System.Globalization;

namespace ProfitFox.Application.MarketData;

/// <summary>Times in the API: ISO 8601 with an explicit offset or <c>Z</c>; without one, parsers would assume the server's time zone.</summary>
public static class ApiTimestamps
{
    private static readonly string[] Formats = ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    public static bool TryParse(string text, out DateTimeOffset time)
    {
        return DateTimeOffset.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time);
    }

    public static string Invalid(string parameter)
    {
        return $"{parameter} must be an ISO 8601 time with an offset, e.g. 2024-01-01T00:00:00Z.";
    }
}