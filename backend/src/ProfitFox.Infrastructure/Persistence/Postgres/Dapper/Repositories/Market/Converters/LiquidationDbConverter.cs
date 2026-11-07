using ProfitFox.Application.Market;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;

internal static class LiquidationDbConverter
{
    public static LiquidationEvent ToDomain(LiquidationDb row)
    {
        return new LiquidationEvent(
            row.Time,
            MarketCodes.TryParseLiquidationSide(row.Side, out LiquidationSide side) ? side : throw new InvalidOperationException($"Unknown liquidation side code '{row.Side}' in storage."),
            row.Price,
            row.Quantity);
    }
}