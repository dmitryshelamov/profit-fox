using Dapper;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;

internal static class IdeaAutoRepositoryQueries
{
    private const string Columns = """
                                   id, name, account_id AS "AccountId", amount, mode, setups, types, intervals, side, category, badge,
                                   min_risk_reward AS "MinRiskReward", min_cases AS "MinCases", position, deleted_at AS "DeletedAt", budget,
                                   day_drop AS "DayDrop", btc_drop AS "BtcDrop", high_funding AS "HighFunding", burst,
                                   max_per_minute_per_side AS "MaxPerMinutePerSide", max_open_per_side AS "MaxOpenPerSide"
                                   """;

    public static CommandDefinition GetAll(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_autos WHERE deleted_at IS NULL ORDER BY position, id", cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetEnabled(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_autos WHERE deleted_at IS NULL AND mode <> 'off' ORDER BY position, id", cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetEvery(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_autos", cancellationToken: cancellationToken);
    }

    public static CommandDefinition Get(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_autos WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken);
    }

    // A share lock: a change of the mode and a delete, which update the row, wait for the entry that reads it.
    public static CommandDefinition Lock(Guid id, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_autos WHERE id = @Id FOR SHARE", new { Id = id }, transaction, cancellationToken: cancellationToken);
    }

    // The new auto stands after the last one that is not deleted.
    public static CommandDefinition Create(IdeaAutoDb auto, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO idea_autos (id, name, account_id, amount, mode, setups, types, intervals, side, category, badge, min_risk_reward, min_cases, position, created_at, budget,
                day_drop, btc_drop, high_funding, burst, max_per_minute_per_side, max_open_per_side)
            VALUES (@Id, @Name, @AccountId, @Amount, @Mode, @Setups, @Types, @Intervals, @Side, @Category, @Badge, @MinRiskReward, @MinCases,
                (SELECT COALESCE(MAX(position) + 1, 0) FROM idea_autos WHERE deleted_at IS NULL), @Now, @Budget, @DayDrop, @BtcDrop, @HighFunding, @Burst,
                @MaxPerMinutePerSide, @MaxOpenPerSide)
            RETURNING position
            """,
            new
            {
                auto.Id,
                auto.Name,
                auto.AccountId,
                auto.Amount,
                auto.Mode,
                auto.Setups,
                auto.Types,
                auto.Intervals,
                auto.Side,
                auto.Category,
                auto.Badge,
                auto.MinRiskReward,
                auto.MinCases,
                auto.Budget,
                auto.DayDrop,
                auto.BtcDrop,
                auto.HighFunding,
                auto.Burst,
                auto.MaxPerMinutePerSide,
                auto.MaxOpenPerSide,
                Now = now
            },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Save(IdeaAutoDb auto, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            UPDATE idea_autos SET name = @Name, account_id = @AccountId, amount = @Amount, setups = @Setups, types = @Types, intervals = @Intervals,
                side = @Side, category = @Category, badge = @Badge, min_risk_reward = @MinRiskReward, min_cases = @MinCases, budget = @Budget,
                day_drop = @DayDrop, btc_drop = @BtcDrop, high_funding = @HighFunding, burst = @Burst,
                max_per_minute_per_side = @MaxPerMinutePerSide, max_open_per_side = @MaxOpenPerSide
            WHERE id = @Id AND deleted_at IS NULL
            """,
            auto,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition SetMode(Guid id, string mode, CancellationToken cancellationToken)
    {
        return new CommandDefinition("UPDATE idea_autos SET mode = @Mode WHERE id = @Id AND deleted_at IS NULL", new { Id = id, Mode = mode }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Reorder(Guid[] ids, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            UPDATE idea_autos SET position = ordered.place - 1
            FROM unnest(@Ids) WITH ORDINALITY AS ordered(auto_id, place)
            WHERE idea_autos.id = ordered.auto_id AND idea_autos.deleted_at IS NULL
            """,
            new { Ids = ids },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Delete(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return new CommandDefinition("UPDATE idea_autos SET deleted_at = @Now WHERE id = @Id AND deleted_at IS NULL", new { Id = id, Now = now }, cancellationToken: cancellationToken);
    }
}