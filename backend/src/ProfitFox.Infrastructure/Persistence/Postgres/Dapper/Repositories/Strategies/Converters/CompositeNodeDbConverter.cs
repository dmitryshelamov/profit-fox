using System.Text.Json;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;

internal static class CompositeNodeDbConverter
{
    internal static CompositeNodeDb FromDomain(CompositeNode node)
    {
        return new CompositeNodeDb
        {
            Id = node.Id,
            Name = node.Name,
            Definition = StrategyJson.WriteComposite(node.Definition).ToJsonString(),
            CreatedAt = node.CreatedAt,
            UpdatedAt = node.UpdatedAt
        };
    }

    internal static CompositeNode ToDomain(CompositeNodeDb node)
    {
        using var definition = JsonDocument.Parse(node.Definition);

        return new CompositeNode(node.Id, node.Name, StrategyJson.ParseComposite(definition.RootElement), node.CreatedAt, node.UpdatedAt);
    }
}