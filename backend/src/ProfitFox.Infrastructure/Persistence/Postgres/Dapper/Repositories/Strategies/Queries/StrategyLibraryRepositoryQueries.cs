using System.Data;

using Dapper;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Queries;

internal static class StrategyLibraryRepositoryQueries
{
    // Lists follow the order of creation; the id breaks ties so the order is stable.
    private const string OrderSql = "ORDER BY created_at, id";

    private const string RevisionSql = "SELECT revision FROM strategy_library WHERE id = 1";

    // Under READ COMMITTED a concurrent bump of the same revision waits for this one and then matches no row.
    private const string BumpSql = "UPDATE strategy_library SET revision = revision + 1 WHERE id = 1 AND revision = @Expected";

    private const string SelectStrategiesSql = """
                                               SELECT id, name, definition::text AS "Definition", created_at AS "CreatedAt", updated_at AS "UpdatedAt"
                                               FROM strategies
                                               """;

    private const string SelectCompositesSql = """
                                               SELECT id, name, definition::text AS "Definition", created_at AS "CreatedAt", updated_at AS "UpdatedAt"
                                               FROM strategy_composite_nodes
                                               """;

    private const string UpsertStrategySql = """
                                             INSERT INTO strategies (id, name, definition, created_at, updated_at)
                                             VALUES (@Id, @Name, @Definition::jsonb, @CreatedAt, @UpdatedAt)
                                             ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, definition = EXCLUDED.definition, updated_at = EXCLUDED.updated_at
                                             """;

    private const string UpsertCompositeSql = """
                                              INSERT INTO strategy_composite_nodes (id, name, definition, created_at, updated_at)
                                              VALUES (@Id, @Name, @Definition::jsonb, @CreatedAt, @UpdatedAt)
                                              ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, definition = EXCLUDED.definition, updated_at = EXCLUDED.updated_at
                                              """;

    internal static CommandDefinition Revision(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RevisionSql, transaction: transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Bump(long expected, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(BumpSql, new { Expected = expected }, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Strategies(IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectStrategiesSql} {OrderSql}", transaction: transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Strategy(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectStrategiesSql} WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Composites(IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectCompositesSql} {OrderSql}", transaction: transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Composite(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectCompositesSql} WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition UpsertStrategy(StrategyDb strategy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpsertStrategySql, strategy, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition UpsertComposite(CompositeNodeDb node, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpsertCompositeSql, node, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition DeleteStrategy(Guid id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition("DELETE FROM strategies WHERE id = @Id", new { Id = id }, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition DeleteComposite(Guid id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition("DELETE FROM strategy_composite_nodes WHERE id = @Id", new { Id = id }, transaction, cancellationToken: cancellationToken);
    }
}