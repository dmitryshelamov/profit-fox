using System.Text.Json;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;

internal static class StrategyVersionDbConverter
{
    internal static StrategyVersionDb FromDomain(StrategyVersion version)
    {
        return new StrategyVersionDb
        {
            StrategyId = version.StrategyId,
            Number = version.Number,
            Snapshot = StrategyJson.WriteSnapshot(version.Snapshot).ToJsonString(),
            CreatedAt = version.CreatedAt
        };
    }

    internal static StrategyVersion ToDomain(StrategyVersionDb version)
    {
        using var snapshot = JsonDocument.Parse(version.Snapshot);

        return new StrategyVersion(version.StrategyId, version.Number, StrategyJson.ParseSnapshot(snapshot.RootElement), version.CreatedAt);
    }
}