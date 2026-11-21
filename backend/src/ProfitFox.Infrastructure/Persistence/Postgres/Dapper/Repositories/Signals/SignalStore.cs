using Dapper;

using Npgsql;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Signals;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals;

/// <summary>
///     The saved signals in the tables <c>signals</c> and <c>signal_versions</c> (spec signal-constructor «Версии
///     сигнала»): a row per signal that changes, and a row per version of its graph that is only added. A save that changes
///     only the layout or the description adds no version, which is told by comparing the graphs as jsonb: by content, not by
///     the order of keys or the spaces.
/// </summary>
public sealed class SignalStore(NpgsqlDataSource database) : ISignalStore
{
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<string>> Names(CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        return [.. await connection.QueryAsync<string>(new CommandDefinition("SELECT name FROM signals", cancellationToken: cancellation))];
    }

    public async Task<bool> Exists(string name, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM signals WHERE name = @Name)", new { Name = name }, cancellationToken: cancellation));
    }

    public async Task<SignalRead?> Read(string name, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        SignalRow? row = await connection.QuerySingleOrDefaultAsync<SignalRow>(new CommandDefinition(
            """
            SELECT s.id AS "Id", s.name AS "Name", s.description AS "Description", s.layout::text AS "Layout", s.version AS "Version", v.graph::text AS "Graph"
            FROM signals s
            JOIN signal_versions v ON v.signal_id = s.id AND v.version = s.version
            WHERE s.name = @Name
            """,
            new { Name = name },
            cancellationToken: cancellation));
        return row is null ? null : SignalJson.ReadStored(row.Name, row.Description, row.Graph, row.Layout, row.Version);
    }

    public async Task<SignalDocument> Create(SignalDocument signal, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellation);
        var id = Guid.NewGuid();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO signals (id, name, description, layout, version) VALUES (@Id, @Name, @Description, @Layout::jsonb, 1)",
                new { Id = id, signal.Name, signal.Description, Layout = SignalJson.WriteLayout(signal) },
                transaction,
                cancellationToken: cancellation));
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            throw Taken(signal.Name);
        }

        await AddVersion(connection, transaction, id, 1, signal, cancellation);
        await transaction.CommitAsync(cancellation);
        return signal with { Version = 1 };
    }

    // The row is locked, so two saves of one signal do not take the same next number.
    public async Task<SignalDocument> Replace(SignalDocument signal, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellation);
        SignalRow? current = await connection.QuerySingleOrDefaultAsync<SignalRow>(new CommandDefinition(
            """SELECT id AS "Id", version AS "Version" FROM signals WHERE name = @Name FOR UPDATE""",
            new { signal.Name },
            transaction,
            cancellationToken: cancellation));
        if (current is null)
        {
            throw NotFound(signal.Name);
        }

        string graph = SignalJson.WriteGraph(signal);
        bool same = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT graph = @Graph::jsonb FROM signal_versions WHERE signal_id = @Id AND version = @Version",
            new { Graph = graph, current.Id, current.Version },
            transaction,
            cancellationToken: cancellation));
        int next = same ? current.Version : current.Version + 1;
        if (!same)
        {
            await AddVersion(connection, transaction, current.Id, next, signal, cancellation);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE signals SET description = @Description, layout = @Layout::jsonb, version = @Version WHERE id = @Id",
            new { signal.Description, Layout = SignalJson.WriteLayout(signal), Version = next, current.Id },
            transaction,
            cancellationToken: cancellation));
        await transaction.CommitAsync(cancellation);
        return signal with { Version = next };
    }

    public async Task Rename(string name, string to, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        try
        {
            int changed = await connection.ExecuteAsync(new CommandDefinition("UPDATE signals SET name = @To WHERE name = @Name", new { Name = name, To = to }, cancellationToken: cancellation));
            if (changed == 0)
            {
                throw NotFound(name);
            }
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            throw Taken(to);
        }
    }

    public async Task Delete(string name, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM signals WHERE name = @Name", new { Name = name }, cancellationToken: cancellation));
    }

    private static Task AddVersion(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, int version, SignalDocument signal, CancellationToken cancellation)
    {
        return connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO signal_versions (signal_id, version, graph) VALUES (@Id, @Version, @Graph::jsonb)",
            new { Id = id, Version = version, Graph = SignalJson.WriteGraph(signal) },
            transaction,
            cancellationToken: cancellation));
    }

    private static SignalLibraryException Taken(string name)
    {
        return new SignalLibraryException(SignalProblem.NameTaken, $"the name {name} is taken");
    }

    private static SignalLibraryException NotFound(string name)
    {
        return new SignalLibraryException(SignalProblem.NotFound, $"there is no signal {name}");
    }
}
