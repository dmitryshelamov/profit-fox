using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
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

    // The thresholds come per interval; high volatility has none of its own.
    private const string CountSql = """
                                    SELECT c.setup, c.type, c."interval", c.side, c.category,
                                        count(*) FILTER (WHERE c.status = 'target')::integer, count(*) FILTER (WHERE c.status = 'stop')::integer,
                                        count(*) FILTER (WHERE c.status = 'expired')::integer, avg(c.risk_reward), sum(c.result)
                                    FROM idea_cases c
                                    JOIN unnest(@intervals, @moves, @volumes, @early_moves, @early_volumes) AS t("interval", move, volume, early_move, early_volume)
                                        ON t."interval" = c."interval"
                                    WHERE c.engine_version = @version AND c.fired_at >= @from
                                        AND ((c.setup IN ('impulse', 'drop') AND c.move >= t.move AND c.volume_ratio >= t.volume)
                                            OR (c.setup IN ('early_impulse', 'early_drop') AND c.move >= t.early_move AND c.volume_ratio >= t.early_volume)
                                            OR c.setup = 'high_volatility')
                                    GROUP BY c.setup, c.type, c."interval", c.side, c.category
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

    internal static NpgsqlCommand Count(NpgsqlConnection connection, int engineVersion, DateTimeOffset from, IReadOnlyDictionary<Interval, SetupThresholds> thresholds)
    {
        var command = new NpgsqlCommand(CountSql, connection);
        command.Parameters.AddRange(
            new[]
            {
                new NpgsqlParameter("version", engineVersion), new NpgsqlParameter("from", from),
                new NpgsqlParameter("intervals", thresholds.Keys.Select(interval => interval.ToCode()).ToArray()),
                new NpgsqlParameter("moves", thresholds.Values.Select(value => value.Move).ToArray()),
                new NpgsqlParameter("volumes", thresholds.Values.Select(value => value.Volume).ToArray()),
                new NpgsqlParameter("early_moves", thresholds.Values.Select(value => value.EarlyMove).ToArray()),
                new NpgsqlParameter("early_volumes", thresholds.Values.Select(value => value.EarlyVolume).ToArray())
            });

        return command;
    }

    internal static CommandDefinition DeleteOtherVersions(int engineVersion, CancellationToken cancellationToken)
    {
        return new CommandDefinition("DELETE FROM idea_cases WHERE engine_version <> @Version", new { Version = engineVersion }, cancellationToken: cancellationToken);
    }
}