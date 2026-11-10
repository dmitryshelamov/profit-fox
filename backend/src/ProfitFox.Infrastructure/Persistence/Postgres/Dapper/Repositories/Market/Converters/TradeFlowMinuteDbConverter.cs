using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;

internal static class TradeFlowMinuteDbConverter
{
    public static TradeFlowMinute ToDomain(TradeFlowMinuteDb row)
    {
        return new TradeFlowMinute(row.OpenTime, row.BuyVolume, row.SellVolume, row.BuyTurnover, row.SellTurnover, row.BuyTrades, row.SellTrades);
    }
}