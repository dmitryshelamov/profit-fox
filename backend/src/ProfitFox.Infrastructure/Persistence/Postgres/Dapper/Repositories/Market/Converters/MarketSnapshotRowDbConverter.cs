using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;

internal static class MarketSnapshotRowDbConverter
{
    internal static MarketSnapshotRowDb FromDomain(MarketSnapshotRow row)
    {
        PairMetrics metrics = row.Metrics;

        return new MarketSnapshotRowDb
        {
            Category = row.Instrument.Category.ToCode(),
            Symbol = row.Instrument.Symbol,
            ComputedAt = row.ComputedAt,
            Price = metrics.Price,
            Change5m = metrics.Change5m,
            Change15m = metrics.Change15m,
            Change1h = metrics.Change1h,
            Change4h = metrics.Change4h,
            Change24h = metrics.Change24h,
            Turnover1h = metrics.Turnover1h,
            Turnover24h = metrics.Turnover24h,
            OpenInterest = metrics.OpenInterest,
            OpenInterestChange1h = metrics.OpenInterestChange1h,
            OpenInterestChange24h = metrics.OpenInterestChange24h,
            FundingRate = metrics.FundingRate,
            Spread = metrics.Spread,
            Incomplete = row.Incomplete
        };
    }

    internal static MarketSnapshotRow ToDomain(MarketSnapshotRowDb row)
    {
        MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);

        return new MarketSnapshotRow(
            new Instrument(category, row.Symbol),
            row.ComputedAt,
            new PairMetrics(
                row.Price,
                row.Change5m,
                row.Change15m,
                row.Change1h,
                row.Change4h,
                row.Change24h,
                row.Turnover1h,
                row.Turnover24h,
                row.OpenInterest,
                row.OpenInterestChange1h,
                row.OpenInterestChange24h,
                row.FundingRate,
                row.Spread),
            row.Incomplete);
    }
}