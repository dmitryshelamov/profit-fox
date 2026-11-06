using System.Runtime.CompilerServices;

using Dapper;

using Npgsql;

using NpgsqlTypes;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     The signals of <c>idea_cases</c> (decision 1). A batch goes in by binary copy into a table of the transaction, then onto its keys: the
///     path of a signal stored already goes on from the new one. The outcome columns of version 4 stay empty.
/// </summary>
internal sealed class IdeaSignalRepository(NpgsqlDataSource dataSource) : IIdeaSignalRepository
{
    private const string Columns = """
                                   category, symbol, setup, type, "interval", side, fired_at, engine_version, entry, stop, target, move, volume_ratio, tick,
                                   entry_after, stop_before_entry_after, stop_after, ladder_after, target_after, closes, tracked_until
                                   """;

    public async Task UpsertAsync(IReadOnlyCollection<IdeaSignal> signals, CancellationToken cancellationToken)
    {
        if (signals.Count == 0)
        {
            return;
        }

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"CREATE TEMP TABLE signals_in ON COMMIT DROP AS SELECT {Columns} FROM idea_cases WITH NO DATA",
            transaction: transaction,
            cancellationToken: cancellationToken));

        await using (NpgsqlBinaryImporter importer = await connection.BeginBinaryImportAsync($"COPY signals_in ({Columns}) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (SignalDb row in signals.Select(SignalDbConverter.FromDomain))
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(row.Category, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(row.Symbol, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(row.Setup, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(row.Type, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(row.Interval, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(row.Side, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(row.FiredAt, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(row.EngineVersion, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(row.Entry, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(row.Stop, NpgsqlDbType.Numeric, cancellationToken);
                await WriteAsync(importer, row.Target, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(row.Move, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(row.VolumeRatio, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(row.Tick, NpgsqlDbType.Numeric, cancellationToken);
                await WriteAsync(importer, row.EntryAfter, NpgsqlDbType.Integer, cancellationToken);
                await WriteAsync(importer, row.StopBeforeEntryAfter, NpgsqlDbType.Integer, cancellationToken);
                await WriteAsync(importer, row.StopAfter, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(row.LadderAfter, NpgsqlDbType.Array | NpgsqlDbType.Integer, cancellationToken);
                await WriteAsync(importer, row.TargetAfter, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(row.Closes, NpgsqlDbType.Array | NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(row.TrackedUntil, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"""
             INSERT INTO idea_cases ({Columns}) SELECT {Columns} FROM signals_in
             ON CONFLICT ON CONSTRAINT "PK_idea_cases" DO UPDATE SET entry = EXCLUDED.entry, entry_after = EXCLUDED.entry_after,
                 stop_before_entry_after = EXCLUDED.stop_before_entry_after, stop_after = EXCLUDED.stop_after, ladder_after = EXCLUDED.ladder_after,
                 target_after = EXCLUDED.target_after, closes = EXCLUDED.closes, tracked_until = EXCLUDED.tracked_until
             """,
            transaction: transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async IAsyncEnumerable<IdeaSignal> StreamAsync(
        int engineVersion,
        DateTimeOffset from,
        (IdeaType Type, Interval Interval)? only,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
             SELECT {Columns} FROM idea_cases
             WHERE engine_version = @version AND fired_at >= @from AND (@type::text IS NULL OR (type = @type AND "interval" = @interval))
             ORDER BY category, symbol, setup, type, "interval", side, fired_at
             """,
            connection);
        command.Parameters.AddWithValue("version", engineVersion);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = (object?)only?.Type.ToCode() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("interval", NpgsqlDbType.Text) { Value = (object?)only?.Interval.ToCode() ?? DBNull.Value });
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            yield return SignalDbConverter.ToDomain(Row(reader));
        }
    }

    public async Task<IReadOnlyList<IdeaSignal>> GetAsync(Instrument instrument, int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
             SELECT {Columns} FROM idea_cases WHERE category = @category AND symbol = @symbol AND engine_version = @version
             ORDER BY fired_at, setup, type, "interval", side
             """,
            connection);
        command.Parameters.AddWithValue("category", instrument.Category.ToCode());
        command.Parameters.AddWithValue("symbol", instrument.Symbol);
        command.Parameters.AddWithValue("version", engineVersion);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var signals = new List<IdeaSignal>();

        while (await reader.ReadAsync(cancellationToken))
        {
            signals.Add(SignalDbConverter.ToDomain(Row(reader)));
        }

        return signals;
    }

    public async Task DeleteAsync(Instrument instrument, int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM idea_cases WHERE category = @Category AND symbol = @Symbol AND engine_version = @Version",
            new { Category = instrument.Category.ToCode(), instrument.Symbol, Version = engineVersion },
            cancellationToken: cancellationToken));
    }

    // One statement over every case of an old version outran the command timeout on prod (24 M cases), so an hour of them goes at a time.
    public async Task<int> DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            """
            WITH first AS (SELECT min(fired_at) AS at FROM idea_cases WHERE engine_version <> @Version)
            DELETE FROM idea_cases USING first
            WHERE engine_version <> @Version AND fired_at >= first.at AND fired_at < first.at + interval '1 hour'
            """,
            new { Version = engineVersion },
            cancellationToken: cancellationToken));
    }

    private static Task WriteAsync<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct
    {
        return value is { } some ? importer.WriteAsync(some, type, cancellationToken) : importer.WriteNullAsync(cancellationToken);
    }

    // A row of the columns in the order of Columns.
    private static SignalDb Row(NpgsqlDataReader reader)
    {
        return new SignalDb
        {
            Category = reader.GetString(0),
            Symbol = reader.GetString(1),
            Setup = reader.GetString(2),
            Type = reader.GetString(3),
            Interval = reader.GetString(4),
            Side = reader.GetString(5),
            FiredAt = reader.GetFieldValue<DateTimeOffset>(6),
            EngineVersion = reader.GetInt32(7),
            Entry = reader.GetDecimal(8),
            Stop = reader.GetDecimal(9),
            Target = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
            Move = reader.GetDecimal(11),
            VolumeRatio = reader.GetDecimal(12),
            Tick = reader.GetDecimal(13),
            EntryAfter = reader.IsDBNull(14) ? null : reader.GetInt32(14),
            StopBeforeEntryAfter = reader.IsDBNull(15) ? null : reader.GetInt32(15),
            StopAfter = reader.IsDBNull(16) ? null : reader.GetInt32(16),
            LadderAfter = reader.GetFieldValue<int?[]>(17),
            TargetAfter = reader.IsDBNull(18) ? null : reader.GetInt32(18),
            Closes = reader.GetFieldValue<decimal[]>(19),
            TrackedUntil = reader.GetFieldValue<DateTimeOffset>(20)
        };
    }
}