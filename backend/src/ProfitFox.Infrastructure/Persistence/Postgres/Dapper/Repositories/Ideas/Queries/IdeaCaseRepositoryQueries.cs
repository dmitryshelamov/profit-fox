using Dapper;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

internal static class IdeaCaseRepositoryQueries
{
    // One statement for a batch; a case of a key, fired time and version already stored stays.
    private const string InsertSql = """
                                     INSERT INTO idea_cases (category, symbol, setup, type, "interval", side, fired_at, engine_version, entry, stop, target,
                                         risk_reward, status, status_at, entered_at, exit_price, move, volume_ratio, result)
                                     SELECT * FROM unnest(@categories, @symbols, @setups, @types, @intervals, @sides, @fired_ats, @versions, @entries, @stops,
                                         @targets, @risk_rewards, @statuses, @status_ats, @entered_ats, @exit_prices, @moves, @volume_ratios, @results)
                                     ON CONFLICT DO NOTHING
                                     """;

    // Decision 7: the replay runs by the live thresholds, so every case of the version counts. Postgres keeps every digit of a total of
    // long fractions, more than System.Decimal holds: it comes rounded to 12 places.
    private const string CountSql = """
                                    SELECT setup, type, "interval", side, category,
                                        count(*) FILTER (WHERE status = 'target')::integer, count(*) FILTER (WHERE status = 'stop')::integer,
                                        count(*) FILTER (WHERE status = 'expired')::integer, avg(risk_reward), round(sum(result), 12)
                                    FROM idea_cases
                                    WHERE engine_version = @version AND fired_at >= @from
                                    GROUP BY setup, type, "interval", side, category
                                    """;

    internal static NpgsqlCommand Insert(NpgsqlDataSource dataSource, IReadOnlyCollection<IdeaDb> cases)
    {
        NpgsqlCommand command = dataSource.CreateCommand(InsertSql);
        command.Parameters.AddRange(
            new[]
            {
                new NpgsqlParameter("categories", cases.Select(c => c.Category).ToArray()), new NpgsqlParameter("symbols", cases.Select(c => c.Symbol).ToArray()),
                new NpgsqlParameter("setups", cases.Select(c => c.Setup).ToArray()), new NpgsqlParameter("types", cases.Select(c => c.Type).ToArray()),
                new NpgsqlParameter("intervals", cases.Select(c => c.Interval).ToArray()), new NpgsqlParameter("sides", cases.Select(c => c.Side).ToArray()),
                new NpgsqlParameter("fired_ats", cases.Select(c => c.FiredAt).ToArray()), new NpgsqlParameter("versions", cases.Select(c => c.EngineVersion).ToArray()),
                new NpgsqlParameter("entries", cases.Select(c => c.Entry).ToArray()), new NpgsqlParameter("stops", cases.Select(c => c.Stop).ToArray()),
                new NpgsqlParameter("targets", cases.Select(c => c.Target).ToArray()), new NpgsqlParameter("risk_rewards", cases.Select(c => c.RiskReward).ToArray()),
                new NpgsqlParameter("statuses", cases.Select(c => c.Status).ToArray()), new NpgsqlParameter("status_ats", cases.Select(c => c.StatusAt).ToArray()),
                new NpgsqlParameter("entered_ats", cases.Select(c => c.EnteredAt!.Value).ToArray()),
                new NpgsqlParameter("exit_prices", cases.Select(c => c.ExitPrice!.Value).ToArray()), new NpgsqlParameter("moves", cases.Select(c => c.Move).ToArray()),
                new NpgsqlParameter("volume_ratios", cases.Select(c => c.VolumeRatio).ToArray()), new NpgsqlParameter("results", cases.Select(c => c.Result).ToArray())
            });

        return command;
    }

    internal static CommandDefinition Get(string category, string symbol, int engineVersion, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             SELECT {IdeaRepositoryQueries.Columns}, result FROM idea_cases
             WHERE category = @Category AND symbol = @Symbol AND engine_version = @Version
             ORDER BY fired_at, setup, type, "interval", side
             """,
            new { Category = category, Symbol = symbol, Version = engineVersion },
            cancellationToken: cancellationToken);
    }

    internal static NpgsqlCommand Count(NpgsqlConnection connection, int engineVersion, DateTimeOffset from)
    {
        var command = new NpgsqlCommand(CountSql, connection);
        command.Parameters.AddWithValue("version", engineVersion);
        command.Parameters.AddWithValue("from", from);

        return command;
    }

    internal static CommandDefinition DeleteOtherVersions(int engineVersion, CancellationToken cancellationToken)
    {
        return new CommandDefinition("DELETE FROM idea_cases WHERE engine_version <> @Version", new { Version = engineVersion }, cancellationToken: cancellationToken);
    }
}