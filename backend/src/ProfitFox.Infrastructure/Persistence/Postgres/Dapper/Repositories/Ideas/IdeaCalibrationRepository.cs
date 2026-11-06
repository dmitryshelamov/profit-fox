using System.Text.Json;

using Dapper;

using Npgsql;

using NpgsqlTypes;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

internal sealed class IdeaCalibrationRepository(NpgsqlDataSource dataSource) : IIdeaCalibrationRepository
{
    // The category of the report of all categories.
    private const string All = "all";

    public async Task ReplaceAsync(int engineVersion, IReadOnlyCollection<CalibrationReport> reports, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM idea_calibration", transaction: transaction, cancellationToken: cancellationToken));

        foreach (CalibrationReport report in reports)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO idea_calibration (engine_version, days, category, computed_at, report)
                VALUES (@version, @days, @category, @computedAt, @report)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("version", engineVersion);
            command.Parameters.AddWithValue("days", report.Days);
            command.Parameters.AddWithValue("category", report.Category?.ToCode() ?? All);
            command.Parameters.AddWithValue("computedAt", report.ComputedAt);
            command.Parameters.AddWithValue("report", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(CalibrationReportDbConverter.FromDomain(report)));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CalibrationReport?> GetAsync(int engineVersion, int days, MarketCategory? category, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(
                """
                SELECT computed_at AS "ComputedAt", report::text AS "Report" FROM idea_calibration
                WHERE engine_version = @Version AND days = @Days AND category = @Category
                """,
                new { Version = engineVersion, Days = days, Category = category?.ToCode() ?? All },
                cancellationToken: cancellationToken));

        return row is null
            ? null
            : CalibrationReportDbConverter.ToDomain(JsonSerializer.Deserialize<CalibrationReportDb>(row.Report)!, row.ComputedAt, days, category);
    }

    private sealed class Row
    {
        public DateTimeOffset ComputedAt { get; init; }

        public required string Report { get; init; }
    }
}