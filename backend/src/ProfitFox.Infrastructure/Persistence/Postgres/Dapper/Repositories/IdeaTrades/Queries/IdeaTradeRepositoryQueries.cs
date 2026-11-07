using Dapper;

using Npgsql;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Queries;

internal static class IdeaTradeRepositoryQueries
{
    private const string TradeColumns = """
                                        id, idea_id AS "IdeaId", account_id AS "AccountId", category, symbol, setup, interval, type, side,
                                        entry_price AS "EntryPrice", stop, target, quantity, amount, risk, entry_deadline AS "EntryDeadline",
                                        status, status_at AS "StatusAt", reason, cancel_requested AS "CancelRequested", close_requested AS "CloseRequested",
                                        entry_quantity AS "EntryQuantity", entry_value AS "EntryValue", entry_fee AS "EntryFee",
                                        take_profit_quantity AS "TakeProfitQuantity", take_profit_value AS "TakeProfitValue", take_profit_fee AS "TakeProfitFee",
                                        stop_loss_quantity AS "StopLossQuantity", stop_loss_value AS "StopLossValue", stop_loss_fee AS "StopLossFee",
                                        manual_quantity AS "ManualQuantity", manual_value AS "ManualValue", manual_fee AS "ManualFee",
                                        last_order_number AS "LastOrderNumber", created_at AS "CreatedAt", lease_epoch AS "LeaseEpoch",
                                        promised_badge AS "PromisedBadge", promised_probability AS "PromisedProbability", promised_expectancy AS "PromisedExpectancy",
                                        promised_cases AS "PromisedCases", break_even AS "BreakEven", net_risk_reward AS "NetRiskReward",
                                        idea_status AS "IdeaStatus", idea_status_at AS "IdeaStatusAt", idea_result AS "IdeaResult", opened_at AS "OpenedAt", auto_id AS "AutoId",
                                        leverage
                                        """;

    private const string OrderColumns = """
                                        trade_id AS "TradeId", number, role, side, type, time_in_force AS "TimeInForce", price, quantity,
                                        take_profit AS "TakeProfit", stop_loss AS "StopLoss", reduce_only AS "ReduceOnly", status,
                                        filled_quantity AS "FilledQuantity", filled_value AS "FilledValue", fee, exchange_order_id AS "ExchangeOrderId",
                                        reject_reason AS "RejectReason", attempts, next_attempt_at AS "NextAttemptAt", created_at AS "CreatedAt",
                                        updated_at AS "UpdatedAt"
                                        """;

    // A Worker keeps a trade that waits for its entry or is open.
    private const string Kept = "status IN ('awaiting_entry', 'open')";

    // It also keeps a failed trade while an order of it may still stand on the exchange, the entry that Bybit would not cancel.
    private const string Followed = "(status IN ('awaiting_entry', 'open') OR (status = 'failed' AND EXISTS (SELECT 1 FROM idea_trade_orders WHERE trade_id = idea_trades.id AND status IN ('pending_submit', 'active', 'pending_cancel', 'cancel_sent'))))";

    private const string Unfinished = "status IN ('pending_submit', 'active', 'pending_cancel', 'cancel_sent')";

    public static CommandDefinition Get(Guid tradeId, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {TradeColumns} FROM idea_trades WHERE id = @TradeId", new { TradeId = tradeId }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetAll(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {TradeColumns} FROM idea_trades ORDER BY created_at DESC, id", cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetOpen(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {TradeColumns} FROM idea_trades WHERE {Kept} ORDER BY created_at, id", cancellationToken: cancellationToken);
    }

    public static CommandDefinition Claim(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             UPDATE idea_trades SET lease_owner = @Owner, leased_until = @LeasedUntil, lease_epoch = lease_epoch + 1
             WHERE id = (
                 SELECT id FROM idea_trades
                 WHERE {Followed} AND (leased_until IS NULL OR leased_until <= @Now)
                 ORDER BY id
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED)
             RETURNING {TradeColumns}
             """,
            new { Owner = owner, Now = now, LeasedUntil = leasedUntil },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition RenewAll(Guid owner, Guid[] tradeIds, long[] epochs, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             UPDATE idea_trades SET leased_until = @LeasedUntil
             FROM unnest(@TradeIds, @Epochs) AS held(held_trade, held_epoch)
             WHERE id = held_trade AND lease_epoch = held_epoch AND lease_owner = @Owner
             RETURNING {TradeColumns}
             """,
            new { Owner = owner, TradeIds = tradeIds, Epochs = epochs, LeasedUntil = leasedUntil },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetChanged(Guid owner, DateTimeOffset since, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"SELECT {TradeColumns}, updated_at AS \"UpdatedAt\" FROM idea_trades WHERE lease_owner = @Owner AND updated_at > @Since",
            new { Owner = owner, Since = since },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Release(Guid tradeId, Guid owner, long epoch, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "UPDATE idea_trades SET lease_owner = NULL, leased_until = NULL WHERE id = @TradeId AND lease_owner = @Owner AND lease_epoch = @Epoch",
            new { TradeId = tradeId, Owner = owner, Epoch = epoch },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition ReleaseAll(Guid owner, Guid[] tradeIds, long[] epochs, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            UPDATE idea_trades SET lease_owner = NULL, leased_until = NULL
            FROM unnest(@TradeIds, @Epochs) AS held(held_trade, held_epoch)
            WHERE id = held_trade AND lease_epoch = held_epoch AND lease_owner = @Owner
            """,
            new { Owner = owner, TradeIds = tradeIds, Epochs = epochs },
            cancellationToken: cancellationToken);
    }

    // An advisory lock of the transaction: it touches no row, so it waits for nothing but other entries.
    public static CommandDefinition LockCreation(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition("SELECT pg_advisory_xact_lock(hashtextextended('idea-trades:create', 0))", transaction: transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Create(IdeaTradeDb trade, DateTimeOffset now, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO idea_trades (
                id, idea_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk, entry_deadline,
                status, status_at, reason, cancel_requested, close_requested, entry_quantity, entry_value, entry_fee,
                take_profit_quantity, take_profit_value, take_profit_fee, stop_loss_quantity, stop_loss_value, stop_loss_fee,
                manual_quantity, manual_value, manual_fee, last_order_number, created_at, lease_epoch, updated_at,
                promised_badge, promised_probability, promised_expectancy, promised_cases, break_even, net_risk_reward, auto_id, leverage)
            VALUES (
                @Id, @IdeaId, @AccountId, @Category, @Symbol, @Setup, @Interval, @Type, @Side, @EntryPrice, @Stop, @Target, @Quantity, @Amount, @Risk, @EntryDeadline,
                @Status, @StatusAt, @Reason, @CancelRequested, @CloseRequested, @EntryQuantity, @EntryValue, @EntryFee,
                @TakeProfitQuantity, @TakeProfitValue, @TakeProfitFee, @StopLossQuantity, @StopLossValue, @StopLossFee,
                @ManualQuantity, @ManualValue, @ManualFee, @LastOrderNumber, @CreatedAt, 0, @Now,
                @PromisedBadge, @PromisedProbability, @PromisedExpectancy, @PromisedCases, @BreakEven, @NetRiskReward, @AutoId, @Leverage)
            """,
            new
            {
                trade.Id,
                trade.IdeaId,
                trade.AccountId,
                trade.Category,
                trade.Symbol,
                trade.Setup,
                trade.Interval,
                trade.Type,
                trade.Side,
                trade.EntryPrice,
                trade.Stop,
                trade.Target,
                trade.Quantity,
                trade.Amount,
                trade.Risk,
                trade.EntryDeadline,
                trade.Status,
                trade.StatusAt,
                trade.Reason,
                trade.CancelRequested,
                trade.CloseRequested,
                trade.EntryQuantity,
                trade.EntryValue,
                trade.EntryFee,
                trade.TakeProfitQuantity,
                trade.TakeProfitValue,
                trade.TakeProfitFee,
                trade.StopLossQuantity,
                trade.StopLossValue,
                trade.StopLossFee,
                trade.ManualQuantity,
                trade.ManualValue,
                trade.ManualFee,
                trade.LastOrderNumber,
                trade.CreatedAt,
                trade.PromisedBadge,
                trade.PromisedProbability,
                trade.PromisedExpectancy,
                trade.PromisedCases,
                trade.BreakEven,
                trade.NetRiskReward,
                trade.AutoId,
                trade.Leverage,
                Now = now
            },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition SelectLocked(Guid tradeId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {TradeColumns} FROM idea_trades WHERE id = @TradeId FOR UPDATE", new { TradeId = tradeId }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition LockLeased(Guid tradeId, Guid owner, long epoch, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"SELECT {TradeColumns} FROM idea_trades WHERE id = @TradeId AND lease_owner = @Owner AND lease_epoch = @Epoch FOR UPDATE",
            new { TradeId = tradeId, Owner = owner, Epoch = epoch },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Save(IdeaTradeDb trade, DateTimeOffset now, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            UPDATE idea_trades SET status = @Status, status_at = @StatusAt, reason = @Reason, cancel_requested = @CancelRequested, close_requested = @CloseRequested,
                entry_quantity = @EntryQuantity, entry_value = @EntryValue, entry_fee = @EntryFee,
                take_profit_quantity = @TakeProfitQuantity, take_profit_value = @TakeProfitValue, take_profit_fee = @TakeProfitFee,
                stop_loss_quantity = @StopLossQuantity, stop_loss_value = @StopLossValue, stop_loss_fee = @StopLossFee,
                manual_quantity = @ManualQuantity, manual_value = @ManualValue, manual_fee = @ManualFee,
                last_order_number = @LastOrderNumber, opened_at = @OpenedAt, updated_at = @Now
            WHERE id = @Id
            """,
            new
            {
                trade.Id,
                trade.Status,
                trade.StatusAt,
                trade.Reason,
                trade.CancelRequested,
                trade.CloseRequested,
                trade.EntryQuantity,
                trade.EntryValue,
                trade.EntryFee,
                trade.TakeProfitQuantity,
                trade.TakeProfitValue,
                trade.TakeProfitFee,
                trade.StopLossQuantity,
                trade.StopLossValue,
                trade.StopLossFee,
                trade.ManualQuantity,
                trade.ManualValue,
                trade.ManualFee,
                trade.LastOrderNumber,
                trade.OpenedAt,
                Now = now
            },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetUnfinishedOrders(Guid tradeId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"SELECT {OrderColumns} FROM idea_trade_orders WHERE trade_id = @TradeId AND {Unfinished} ORDER BY number",
            new { TradeId = tradeId },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition SaveOrder(IdeaTradeOrderDb order, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO idea_trade_orders (trade_id, number, role, side, type, time_in_force, price, quantity, take_profit, stop_loss, reduce_only, status, filled_quantity, filled_value, fee, exchange_order_id, reject_reason, attempts, next_attempt_at, created_at, updated_at)
            VALUES (@TradeId, @Number, @Role, @Side, @Type, @TimeInForce, @Price, @Quantity, @TakeProfit, @StopLoss, @ReduceOnly, @Status, @FilledQuantity, @FilledValue, @Fee, @ExchangeOrderId, @RejectReason, @Attempts, @NextAttemptAt, @CreatedAt, @UpdatedAt)
            ON CONFLICT (trade_id, number) DO UPDATE SET
                status = EXCLUDED.status, filled_quantity = EXCLUDED.filled_quantity, filled_value = EXCLUDED.filled_value, fee = EXCLUDED.fee, exchange_order_id = EXCLUDED.exchange_order_id,
                reject_reason = EXCLUDED.reject_reason, attempts = EXCLUDED.attempts, next_attempt_at = EXCLUDED.next_attempt_at,
                updated_at = EXCLUDED.updated_at
            """,
            order,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition SaveChild(IdeaTradeChildDb child, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO idea_trade_children (exchange_order_id, trade_id, leg, filled_quantity, filled_value, fee, finished)
            VALUES (@ExchangeOrderId, @TradeId, @Leg, @FilledQuantity, @FilledValue, @Fee, @Finished)
            ON CONFLICT (exchange_order_id) DO UPDATE SET
                filled_quantity = EXCLUDED.filled_quantity, filled_value = EXCLUDED.filled_value, fee = EXCLUDED.fee, finished = EXCLUDED.finished
            """,
            child,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition AddFill(IdeaTradeFillDb fill, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO idea_trade_fills (exec_id, trade_id, exchange_order_id, quantity, price, fee, fee_asset, executed_at)
            VALUES (@ExecId, @TradeId, @ExchangeOrderId, @Quantity, @Price, @Fee, @FeeAsset, @ExecutedAt)
            ON CONFLICT (exec_id) DO NOTHING
            """,
            fill,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition IsChild(string exchangeOrderId, CancellationToken cancellationToken)
    {
        return new CommandDefinition("SELECT EXISTS (SELECT 1 FROM idea_trade_children WHERE exchange_order_id = @ExchangeOrderId)", new { ExchangeOrderId = exchangeOrderId }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetOrders(Guid tradeId, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {OrderColumns} FROM idea_trade_orders WHERE trade_id = @TradeId ORDER BY number", new { TradeId = tradeId }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetChildren(Guid tradeId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            SELECT trade_id AS "TradeId", exchange_order_id AS "ExchangeOrderId", leg, filled_quantity AS "FilledQuantity", filled_value AS "FilledValue", fee, finished
            FROM idea_trade_children WHERE trade_id = @TradeId ORDER BY exchange_order_id
            """,
            new { TradeId = tradeId },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetFills(Guid tradeId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            SELECT exec_id AS "ExecId", trade_id AS "TradeId", exchange_order_id AS "ExchangeOrderId", quantity, price, fee, fee_asset AS "FeeAsset", executed_at AS "ExecutedAt"
            FROM idea_trade_fills WHERE trade_id = @TradeId ORDER BY executed_at, exec_id
            """,
            new { TradeId = tradeId },
            cancellationToken: cancellationToken);
    }
}