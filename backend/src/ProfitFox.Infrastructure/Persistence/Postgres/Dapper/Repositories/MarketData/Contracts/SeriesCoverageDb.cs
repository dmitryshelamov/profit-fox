namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

internal sealed class SeriesCoverageDb
{
    public DateTimeOffset CoveredFrom { get; init; }

    public DateTimeOffset CoveredUntil { get; init; }
}