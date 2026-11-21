using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ProfitFox.Presentation;

/// <summary>
///     Healthy when the database of the bench answers a query.
/// </summary>
public sealed class DatabaseHealthCheck(NpgsqlDataSource database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellation = default)
    {
        try
        {
            await using NpgsqlCommand command = database.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellation);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database of the bench does not answer.", exception);
        }
    }
}
