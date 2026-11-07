using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Persistence;

public class LiquidationDbConverterTests
{
    [Test]
    public void Unknown_side_is_not_read()
    {
        var row = new LiquidationDb { Time = DateTimeOffset.Parse("2026-11-08T12:04:07Z"), Side = "Sell", Price = 150.2m, Quantity = 12.5m };

        Should.Throw<InvalidOperationException>(() => LiquidationDbConverter.ToDomain(row));
    }
}