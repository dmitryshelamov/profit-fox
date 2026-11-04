namespace ProfitFox.Domain.Entities.SavedFilters.Enums;

/// <summary>A field a condition of a saved filter tests: a metric of the market or a field of an idea.</summary>
public enum FilterField
{
    Price,
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
    RiskReward,
    Probability,
    Expectancy,
    Cases,
    AboveBaseline
}