using System.Data;

using Dapper;

using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Queries;

/// <summary>
///     The statements on <c>signals</c>, a row per signal that changes, and <c>signal_versions</c>, a row per version of its
///     graph that is only added. The graph and the layout go as the JSON <see cref="SignalJson" /> writes.
/// </summary>
internal static class SignalRepositoryQueries
{
    private const string NamesSql = "SELECT name FROM signals";

    private const string ExistsSql = "SELECT EXISTS (SELECT 1 FROM signals WHERE name = @Name)";

    private const string ReadSql =
        """
        SELECT s.id AS "Id", s.name AS "Name", s.description AS "Description", s.layout::text AS "Layout", s.version AS "Version", v.graph::text AS "Graph"
        FROM signals s
        JOIN signal_versions v ON v.signal_id = s.id AND v.version = s.version
        WHERE s.name = @Name
        """;

    private const string InsertSql = "INSERT INTO signals (id, name, description, layout, version) VALUES (@Id, @Name, @Description, @Layout::jsonb, 1)";

    private const string CurrentSql = """SELECT id AS "Id", version AS "Version" FROM signals WHERE name = @Name FOR UPDATE""";

    private const string SameGraphSql = "SELECT graph = @Graph::jsonb FROM signal_versions WHERE signal_id = @Id AND version = @Version";

    private const string UpdateSql = "UPDATE signals SET description = @Description, layout = @Layout::jsonb, version = @Version WHERE id = @Id";

    private const string RenameSql = "UPDATE signals SET name = @To WHERE name = @Name";

    private const string DeleteSql = "DELETE FROM signals WHERE name = @Name";

    private const string InsertVersionSql = "INSERT INTO signal_versions (signal_id, version, graph) VALUES (@Id, @Version, @Graph::jsonb)";

    public static CommandDefinition Names(CancellationToken cancellationToken)
    {
        return new CommandDefinition(NamesSql, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Exists(string name, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ExistsSql, new { Name = name }, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The row of the signal with the graph of its latest version.
    /// </summary>
    public static CommandDefinition Read(string name, CancellationToken cancellationToken)
    {
        return new CommandDefinition(ReadSql, new { Name = name }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Insert(Guid id, SignalDocument signal, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertSql,
            new { Id = id, signal.Name, signal.Description, Layout = SignalJson.WriteLayout(signal) },
            transaction,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The id and the latest version of the signal, its row locked to the end of the transaction.
    /// </summary>
    public static CommandDefinition Current(string name, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(CurrentSql, new { Name = name }, transaction, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Whether the version <paramref name="version" /> of the signal <paramref name="id" /> has the graph
    ///     <paramref name="graph" /> by content.
    /// </summary>
    public static CommandDefinition SameGraph(Guid id, int version, string graph, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(SameGraphSql, new { Graph = graph, Id = id, Version = version }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Update(Guid id, SignalDocument signal, int version, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            UpdateSql,
            new { signal.Description, Layout = SignalJson.WriteLayout(signal), Version = version, Id = id },
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

    public static CommandDefinition InsertVersion(Guid id, int version, SignalDocument signal, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertVersionSql,
            new { Id = id, Version = version, Graph = SignalJson.WriteGraph(signal) },
            transaction,
            cancellationToken: cancellationToken);
    }
}
