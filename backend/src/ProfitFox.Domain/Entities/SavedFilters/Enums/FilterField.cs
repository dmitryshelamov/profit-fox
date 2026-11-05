namespace ProfitFox.Domain.Entities.SavedFilters.Enums;

/// <summary>A field a condition of a saved filter tests: a metric or a setup of the market, or a field of an idea.</summary>
public enum FilterField
{
    Category,
    Change5m,
    Change15m,
    Change1h,
    Change4h,
    Change24h,
    Turnover1h,
    Turnover24h,
    OpenInterest,
    OpenInterestChange1h,
    OpenInterestChange24h,
    FundingRate,
    Spread,
    Setup,
    IdeaSetup,
    IdeaType,
    IdeaInterval,
    IdeaSide,
    RiskReward,
    Probability,
    Expectancy
}