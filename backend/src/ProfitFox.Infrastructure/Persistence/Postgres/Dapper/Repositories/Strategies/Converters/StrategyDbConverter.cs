using System.Text.Json;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;

internal static class StrategyDbConverter
{
    internal static StrategyDb FromDomain(Strategy strategy)
    {
        return new StrategyDb
        {
            Id = strategy.Id,
            Name = strategy.Name,
            Definition = StrategyJson.WriteStrategy(strategy.Definition).ToJsonString(),
            CreatedAt = strategy.CreatedAt,
            UpdatedAt = strategy.UpdatedAt
        };
    }

    internal static Strategy ToDomain(StrategyDb strategy)
    {
        using var definition = JsonDocument.Parse(strategy.Definition);

        return new Strategy(strategy.Id, strategy.Name, StrategyJson.ParseStrategy(definition.RootElement), strategy.CreatedAt, strategy.UpdatedAt);
    }
}