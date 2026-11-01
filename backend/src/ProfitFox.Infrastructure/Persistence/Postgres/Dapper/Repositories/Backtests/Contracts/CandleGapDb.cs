namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

internal sealed record CandleGapDb(DateTimeOffset From, DateTimeOffset To);