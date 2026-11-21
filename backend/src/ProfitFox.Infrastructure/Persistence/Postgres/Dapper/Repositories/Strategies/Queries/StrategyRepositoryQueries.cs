using System.Data;

using Dapper;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies.Queries;

/// <summary>
///     The statements on <c>strategies</c>, a row per strategy that changes, <c>strategy_versions</c>, a row per version of its
///     graph that is only added, and <c>strategy_signals</c>, a row per signal node of a version.
/// </summary>
internal static class StrategyRepositoryQueries
{
    private const string NamesSql = "SELECT name FROM strategies";

    private const string ExistsSql = "SELECT EXISTS (SELECT 1 FROM strategies WHERE name = @Name)";

    private const string ReadSql =
        """
        SELECT s.id AS "Id", s.name AS "Name", s.description AS "Description", s.layout::text AS "Layout", s.version AS "Version", v.graph::text AS "Graph"
        FROM strategies s
        JOIN strategy_versions v ON v.strategy_id = s.id AND v.version = s.version
        WHERE s.name = @Name
        """;

    private const string InsertSql = "INSERT INTO strategies (id, name, description, layout, version) VALUES (@Id, @Name, @Description, @Layout::jsonb, 1)";

    private const string CurrentSql = """SELECT id AS "Id", version AS "Version" FROM strategies WHERE name = @Name FOR UPDATE""";

    private const string SameGraphSql = "SELECT graph = @Graph::jsonb FROM strategy_versions WHERE strategy_id = @Id AND version = @Version";

    private const string UpdateSql = "UPDATE strategies SET description = @Description, layout = @Layout::jsonb, version = @Version WHERE id = @Id";

    private const string RenameSql = "UPDATE strategies SET name = @To WHERE name = @Name";

    private const string DeleteSql = "DELETE FROM strategies WHERE name = @Name";

    private const string InsertVersionSql = "INSERT INTO strategy_versions (strategy_id, version, graph) VALUES (@Id, @Version, @Graph::jsonb)";

    private const string InsertSignalSql =
        """
        INSERT INTO strategy_signals (strategy_id, version, node, signal_id, signal_version)
        VALUES (@StrategyId, @Version, @Node, @SignalId, @SignalVersion)
        """;

    private const string UsingSignalSql =
        """
        SELECT DISTINCT s.name
        FROM strategy_signals x
        JOIN strategies s ON s.id = x.strategy_id
        WHERE x.signal_id = @Signal
        ORDER BY s.name
        """;

    public static CommandDefinition Names(CancellationToken cancellationToken)
    {
        return new CommandDefinition(NamesSql, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Exists(string name, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ExistsSql, new { Name = name }, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The row of the strategy with the graph of its latest version.
    /// </summary>
    public static CommandDefinition Read(string name, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ReadSql, new { Name = name }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Insert(Guid id, StrategyDocument strategy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertSql,
            new { Id = id, strategy.Name, strategy.Description, Layout = StrategyDbConverter.WriteLayout(strategy) },
            transaction,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The id and the latest version of the strategy, its row locked to the end of the transaction.
    /// </summary>
    public static CommandDefinition Current(string name, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(CurrentSql, new { Name = name }, transaction, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Whether the version <paramref name="version" /> of the strategy <paramref name="id" /> has the graph
    ///     <paramref name="graph" /> by content.
    /// </summary>
    public static CommandDefinition SameGraph(Guid id, int version, string graph, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(SameGraphSql, new { Graph = graph, Id = id, Version = version }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Update(Guid id, StrategyDocument strategy, int version, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            UpdateSql,
            new { strategy.Description, Layout = StrategyDbConverter.WriteLayout(strategy), Version = version, Id = id },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Rename(string name, string to, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RenameSql, new { Name = name, To = to }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Delete(string name, CancellationToken cancellationToken)
    {
        return new CommandDefinition(DeleteSql, new { Name = name }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition InsertVersion(Guid id, int version, StrategyDocument strategy, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertVersionSql,
            new { Id = id, Version = version, Graph = StrategyDbConverter.WriteGraph(strategy) },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition InsertSignals(IReadOnlyList<StrategySignalDb> rows, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSignalSql, rows, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition UsingSignal(Guid signal, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UsingSignalSql, new { Signal = signal }, cancellationToken: cancellationToken);
    }
}
