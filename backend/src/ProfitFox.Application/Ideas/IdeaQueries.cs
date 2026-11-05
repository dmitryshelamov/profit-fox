using ProfitFox.Application.MarketData;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.Ideas;

/// <summary>Texts of the field errors of the queries of ideas, and the intervals they take.</summary>
public static class IdeaQueries
{
    public const string InvalidSetup = "Setup must be impulse, early_impulse, drop, early_drop or high_volatility.";
    public const string InvalidType = "Type must be levels, target_2r, beyond_extreme or pullback.";
    public const string InvalidSide = "Side must be long or short.";
    public const string InvalidInterval = "Interval must be 5, 15, 60 or 240.";
    public const string NoShortOnSpot = "Spot has no short ideas.";

    /// <summary>How long an idea that closed stays in the lists.</summary>
    public static readonly TimeSpan ClosedShown = TimeSpan.FromHours(24);

    public static bool TryParseInterval(string? code, out Interval interval)
    {
        return MarketDataCodes.TryParseInterval(code ?? "", out interval) && IdeasSettings.Intervals.Contains(interval);
    }
}