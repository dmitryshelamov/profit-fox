using System.Reflection;

using FluentMigrator;

using Npgsql;

namespace ProfitFox.Infrastructure.Persistence.Postgres;

/// <summary>
///     The version of the schema of the bench database: the one these binaries need, and the one the database has. A Worker
///     does not migrate; it waits until the API has (design add-bench-service, decision 1).
/// </summary>
public static class BenchSchema
{
    private const string UndefinedTable = "42P01";

    /// <summary>
    ///     The version of the latest migration of these binaries.
    /// </summary>
    public static long Expected { get; } = typeof(BenchInfrastructure).Assembly
        .GetTypes()
        .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Version ?? 0)
        .Max();

    /// <summary>
    ///     The version of the latest migration the database has, or 0 when it has none.
    /// </summary>
    public static async Task<long> Current(NpgsqlDataSource database, CancellationToken cancellation)
    {
        await using NpgsqlCommand command = database.CreateCommand("SELECT max(\"Version\") FROM \"VersionInfo\"");
        try
        {
            return await command.ExecuteScalarAsync(cancellation) is long version ? version : 0;
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTable)
        {
            return 0;
        }
    }

    /// <summary>
    ///     Returns when the database has the schema these binaries need, asking every <paramref name="poll" />. A database that
    ///     cannot be reached yet is waited for as well.
    /// </summary>
    public static async Task WaitFor(NpgsqlDataSource database, TimeSpan poll, TextWriter log, CancellationToken cancellation)
    {
        while (true)
        {
            try
            {
                long current = await Current(database, cancellation);
                if (current >= Expected)
                {
                    return;
                }

                log.WriteLine($"The database has the schema {current}, the Worker needs {Expected}. Waiting for the API to migrate it.");
            }
            catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
            {
                log.WriteLine($"The database is not reachable yet: {exception.Message}");
            }

            await Task.Delay(poll, cancellation);
        }
    }
}
