namespace ProfitFox.Domain.Entities.Ideas.Enums;

/// <summary>The badge of an interval of an expectancy: ▲ all of it above 0, ▼ all of it below, ≈ otherwise; none without an interval.</summary>
public static class IdeaBadges
{
    public static IdeaBadge? Of(decimal? low, decimal? high)
    {
        return low switch
        {
            null => null,
            > 0 => IdeaBadge.Up,
            _ => high < 0 ? IdeaBadge.Down : IdeaBadge.Flat
        };
    }
}