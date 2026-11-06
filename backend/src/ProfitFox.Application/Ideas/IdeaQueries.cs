using ProfitFox.Application.MarketData;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.Ideas;

/// <summary>Texts of the field errors of the queries of ideas, and the intervals they take.</summary>
public static class IdeaQueries
{
    // Every code the API takes, in the order of the enums, so a new setup or type is named as it comes.
    public static readonly string InvalidSetup = $"Setup must be {OneOf([.. Enum.GetValues<IdeaSetup>().Select(setup => setup.ToCode())])}.";
    public static readonly string InvalidType = $"Type must be {OneOf([.. Enum.GetValues<IdeaType>().Select(type => type.ToCode())])}.";
    public const string InvalidSide = "Side must be long or short.";
    public const string InvalidInterval = "Interval must be 5, 15, 60 or 240.";
    public const string NoShortOnSpot = "Spot has no short ideas.";

    public const string NoOwnStopEntry = "A stop entry comes only with a setup: without one it has no range.";

    /// <summary>How long an idea that closed stays in the lists.</summary>
    public static readonly TimeSpan ClosedShown = TimeSpan.FromHours(24);

    private static string OneOf(string[] codes)
    {
        return $"{string.Join(", ", codes[..^1])} or {codes[^1]}";
    }

    public static bool TryParseInterval(string? code, out Interval interval)
    {
        return MarketDataCodes.TryParseInterval(code ?? "", out interval) && IdeasSettings.Intervals.Contains(interval);
    }
}