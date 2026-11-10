using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;

internal static class TradeFlowDayDbConverter
{
    public static TradeFlowDay ToDomain(TradeFlowDayDb row)
    {
        MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);

        return new TradeFlowDay(
            new Instrument(category, row.Symbol),
            row.Day,
            MarketCodes.ParseTradeFlowDayStatus(row.Status),
            row.At);
    }
}