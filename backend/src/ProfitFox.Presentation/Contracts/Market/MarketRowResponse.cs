using System.Globalization;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>
///     The row of a pair in the market snapshot, with the time it was computed. Numbers are decimal strings, an empty metric is <c>null</c>; changes and spread in
///     percent, turnovers and open interest in USDT.
/// </summary>
public sealed record MarketRowResponse(
    string Category,
    string Symbol,
    DateTimeOffset ComputedAt,
    string? Price,
    string? Change5m,
    string? Change15m,
    string? Change1h,
    string? Change4h,
    string? Change24h,
    string? Turnover1h,
    string? Turnover24h,
    string? OpenInterest,
    string? OpenInterestChange1h,
    string? OpenInterestChange24h,
    string? FundingRate,
    string? Spread,
    bool Incomplete)
{
    public static MarketRowResponse Of(MarketSnapshotRow row)
    {
        PairMetrics metrics = row.Metrics;

        return new MarketRowResponse(
            row.Instrument.Category.ToCode(),
            row.Instrument.Symbol,
            row.ComputedAt,
            Format(metrics.Price),
            Format(metrics.Change5m),
            Format(metrics.Change15m),
            Format(metrics.Change1h),
            Format(metrics.Change4h),
            Format(metrics.Change24h),
            Format(metrics.Turnover1h),
            Format(metrics.Turnover24h),
            Format(metrics.OpenInterest),
            Format(metrics.OpenInterestChange1h),
            Format(metrics.OpenInterestChange24h),
            Format(metrics.FundingRate),
            Format(metrics.Spread),
            row.Incomplete);
    }

    private static string? Format(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}