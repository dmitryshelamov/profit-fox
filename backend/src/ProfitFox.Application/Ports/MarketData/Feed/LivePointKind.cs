namespace ProfitFox.Application.Ports.MarketData.Feed;

/// <summary>
///     The feed a <see cref="LivePoint" /> comes from.
/// </summary>
public enum LivePointKind
{
    FundingPayment,
    FundingRate,
    OpenInterest
}
