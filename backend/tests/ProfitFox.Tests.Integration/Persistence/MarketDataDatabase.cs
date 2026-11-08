using Npgsql;

namespace ProfitFox.Tests.Integration.Persistence;

public static class MarketDataDatabase
{
    /// <summary>A data source of the test database, migrated and emptied before the test.</summary>
    public static NpgsqlDataSource Open()
    {
        return NpgsqlDataSource.Create(IntegrationEnvironment.ConnectionString);
    }
}