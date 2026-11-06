using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;

internal static class ScannerPairDbConverter
{
    internal static ScannerPairDb FromDomain(ScannerPair pair)
    {
        return new ScannerPairDb
        {
            Category = pair.Instrument.Category.ToCode(),
            Symbol = pair.Instrument.Symbol,
            Listed = pair.Listed,
            HistoryFrom = pair.HistoryFrom,
            CoveredUntil = pair.CoveredUntil,
            OiCoveredUntil = pair.OpenInterestCoveredUntil,
            FundingCoveredUntil = pair.FundingCoveredUntil,
            ListedAt = pair.ListedAt,
            TickSize = pair.TickSize,
            TradFi = pair.TradFi
        };
    }

    internal static ScannerPair ToDomain(ScannerPairDb pair)
    {
        MarketDataCodes.TryParseCategory(pair.Category, out MarketCategory category);

        return new ScannerPair(new Instrument(category, pair.Symbol), pair.Listed, pair.HistoryFrom, pair.CoveredUntil, pair.OiCoveredUntil, pair.FundingCoveredUntil, pair.ListedAt, pair.TickSize, pair.TradFi);
    }
}