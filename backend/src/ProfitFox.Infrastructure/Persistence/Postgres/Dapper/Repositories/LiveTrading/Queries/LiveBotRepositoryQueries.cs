using Dapper;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Queries;

internal static class LiveBotRepositoryQueries
{
    private const string BotColumns = """
                                      bot_id AS "BotId", status, stop_reason AS "StopReason", strategy_version AS "StrategyVersion",
                                      parameter_values::text AS "ParameterValues", start_pending AS "StartPending", close_requested AS "CloseRequested",
                                      last_order_number AS "LastOrderNumber", last_bar_at AS "LastBarAt", session_failures AS "SessionFailures",
                                      lease_epoch AS "LeaseEpoch"
                                      """;

    private const string PositionColumns = """
                                           bot_id AS "BotId", number, open, closing, quantity, invested, collateral, realized_pnl AS "RealizedPnl", fees,
                                           last_entry_price AS "LastEntryPrice", last_entry_at AS "LastEntryAt", skipped_orders AS "SkippedOrders",
                                           closed_at AS "ClosedAt"
                                           """;

    private const string OrderColumns = """
                                        bot_id AS "BotId", number, position_number AS "PositionNumber", node_path AS "NodePath", side, type, price,
                                        quantity, amount, reserve, status, filled_quantity AS "FilledQuantity", filled_value AS "FilledValue", fee, exchange_order_id AS "ExchangeOrderId",
                                        reject_reason AS "RejectReason", attempts, next_attempt_at AS "NextAttemptAt", created_at AS "CreatedAt",
                                        updated_at AS "UpdatedAt"
                                        """;

    // A Worker keeps a bot that runs, waits for its cancels or closes its position.
    private const string Kept = "(status IN ('running', 'paused', 'stopping') OR close_requested)";

    private const string Unfinished = "status IN ('pending_submit', 'active', 'pending_cancel', 'cancel_sent')";

    public static CommandDefinition Get(Guid botId, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {BotColumns} FROM live_bots WHERE bot_id = @BotId", new { BotId = botId }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetAll(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {BotColumns} FROM live_bots", cancellationToken: cancellationToken);
    }

    public static CommandDefinition Claim(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             UPDATE live_bots SET lease_owner = @Owner, leased_until = @LeasedUntil, lease_epoch = lease_epoch + 1
             WHERE bot_id = (
                 SELECT bot_id FROM live_bots
                 WHERE {Kept} AND (leased_until IS NULL OR leased_until <= @Now)
                 ORDER BY bot_id
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED)
             RETURNING {BotColumns}
             """,
            new { Owner = owner, Now = now, LeasedUntil = leasedUntil },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Renew(Guid botId, Guid owner, long epoch, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             UPDATE live_bots SET leased_until = @LeasedUntil
             WHERE bot_id = @BotId AND lease_owner = @Owner AND lease_epoch = @Epoch
             RETURNING {BotColumns}
             """,
            new { BotId = botId, Owner = owner, Epoch = epoch, LeasedUntil = leasedUntil },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Release(Guid botId, Guid owner, long epoch, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "UPDATE live_bots SET lease_owner = NULL, leased_until = NULL WHERE bot_id = @BotId AND lease_owner = @Owner AND lease_epoch = @Epoch",
            new { BotId = botId, Owner = owner, Epoch = epoch },
            cancellationToken: cancellationToken);
    }

    // The bot row first, as a save of the strategy library does, so the two never wait for each other in turns.
    public static CommandDefinition Lock(Guid botId, DateTimeOffset now, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            WITH bot AS (SELECT id FROM bots WHERE id = @BotId FOR UPDATE),
            created AS (
                INSERT INTO live_bots (bot_id, status, parameter_values, start_pending, close_requested, last_order_number, lease_epoch, updated_at)
                SELECT id, 'stopped', '{}'::jsonb, false, false, 0, 0, @Now FROM bot
                ON CONFLICT (bot_id) DO NOTHING)
            SELECT id FROM bot
            """,
            new { BotId = botId, Now = now },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition SelectLocked(Guid botId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {BotColumns} FROM live_bots WHERE bot_id = @BotId FOR UPDATE", new { BotId = botId }, transaction, cancellationToken: cancellationToken);
    }

    // An advisory lock of the transaction, keyed by the account: it touches no row, so it waits for nothing but other starts.
    public static CommandDefinition LockAccount(Guid accountId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended('live-account:' || @AccountId::text, 0))",
            new { AccountId = accountId },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition LockLeased(Guid botId, Guid owner, long epoch, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"SELECT {BotColumns} FROM live_bots WHERE bot_id = @BotId AND lease_owner = @Owner AND lease_epoch = @Epoch FOR UPDATE",
            new { BotId = botId, Owner = owner, Epoch = epoch },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Save(LiveBotDb bot, DateTimeOffset now, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            UPDATE live_bots SET status = @Status, stop_reason = @StopReason, strategy_version = @StrategyVersion,
                parameter_values = @ParameterValues::jsonb, start_pending = @StartPending, close_requested = @CloseRequested,
                last_order_number = @LastOrderNumber, last_bar_at = @LastBarAt, session_failures = @SessionFailures, updated_at = @Now
            WHERE bot_id = @BotId
            """,
            new
            {
                bot.BotId,
                bot.Status,
                bot.StopReason,
                bot.StrategyVersion,
                bot.ParameterValues,
                bot.StartPending,
                bot.CloseRequested,
                bot.LastOrderNumber,
                bot.LastBarAt,
                bot.SessionFailures,
                Now = now
            },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetCurrentPosition(Guid botId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"SELECT {PositionColumns} FROM live_positions WHERE bot_id = @BotId ORDER BY number DESC LIMIT 1",
            new { BotId = botId },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetPastPnl(Guid botId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "SELECT coalesce(sum(realized_pnl), 0) FROM live_positions WHERE bot_id = @BotId AND closed_at IS NOT NULL",
            new { BotId = botId },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition SavePosition(LivePositionDb position, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO live_positions (bot_id, number, open, closing, quantity, invested, collateral, realized_pnl, fees, last_entry_price, last_entry_at, skipped_orders, closed_at)
            VALUES (@BotId, @Number, @Open, @Closing, @Quantity, @Invested, @Collateral, @RealizedPnl, @Fees, @LastEntryPrice, @LastEntryAt, @SkippedOrders, @ClosedAt)
            ON CONFLICT (bot_id, number) DO UPDATE SET
                open = EXCLUDED.open, closing = EXCLUDED.closing, quantity = EXCLUDED.quantity, invested = EXCLUDED.invested, collateral = EXCLUDED.collateral,
                realized_pnl = EXCLUDED.realized_pnl, fees = EXCLUDED.fees, last_entry_price = EXCLUDED.last_entry_price,
                last_entry_at = EXCLUDED.last_entry_at, skipped_orders = EXCLUDED.skipped_orders, closed_at = EXCLUDED.closed_at
            """,
            position,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetUnfinishedOrders(Guid botId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"SELECT {OrderColumns} FROM live_orders WHERE bot_id = @BotId AND {Unfinished} ORDER BY number",
            new { BotId = botId },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition SaveOrder(LiveOrderDb order, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO live_orders (bot_id, number, position_number, node_path, side, type, price, quantity, amount, reserve, status, filled_quantity, filled_value, fee, exchange_order_id, reject_reason, attempts, next_attempt_at, created_at, updated_at)
            VALUES (@BotId, @Number, @PositionNumber, @NodePath, @Side, @Type, @Price, @Quantity, @Amount, @Reserve, @Status, @FilledQuantity, @FilledValue, @Fee, @ExchangeOrderId, @RejectReason, @Attempts, @NextAttemptAt, @CreatedAt, @UpdatedAt)
            ON CONFLICT (bot_id, number) DO UPDATE SET
                status = EXCLUDED.status, filled_quantity = EXCLUDED.filled_quantity, filled_value = EXCLUDED.filled_value, fee = EXCLUDED.fee, exchange_order_id = EXCLUDED.exchange_order_id,
                reject_reason = EXCLUDED.reject_reason, attempts = EXCLUDED.attempts, next_attempt_at = EXCLUDED.next_attempt_at,
                updated_at = EXCLUDED.updated_at
            """,
            order,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition AddFill(LiveFillDb fill, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO live_fills (exec_id, bot_id, order_number, quantity, price, fee, fee_asset, executed_at)
            VALUES (@ExecId, @BotId, @OrderNumber, @Quantity, @Price, @Fee, @FeeAsset, @ExecutedAt)
            ON CONFLICT (exec_id) DO NOTHING
            """,
            fill,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetPositions(Guid botId, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {PositionColumns} FROM live_positions WHERE bot_id = @BotId ORDER BY number DESC", new { BotId = botId }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetOrders(Guid botId, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {OrderColumns} FROM live_orders WHERE bot_id = @BotId ORDER BY number", new { BotId = botId }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetFills(Guid botId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            SELECT exec_id AS "ExecId", bot_id AS "BotId", order_number AS "OrderNumber", quantity, price, fee, fee_asset AS "FeeAsset", executed_at AS "ExecutedAt"
            FROM live_fills WHERE bot_id = @BotId ORDER BY executed_at, exec_id
            """,
            new { BotId = botId },
            cancellationToken: cancellationToken);
    }
}