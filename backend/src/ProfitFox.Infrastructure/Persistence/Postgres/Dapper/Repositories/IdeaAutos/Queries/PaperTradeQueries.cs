using System.Data;

using Dapper;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;

internal static class PaperTradeQueries
{
    // A paper trade has the columns of a real one but those of the lease, the orders and the manual leg, which read as if they were zero, and the
    // columns of the leg of the expiry, which only it has.
    private const string Columns = """
                                   id, idea_id AS "IdeaId", account_id AS "AccountId", auto_id AS "AutoId", category, symbol, setup, interval, type, side,
                                   entry_price AS "EntryPrice", stop, target, quantity, amount, risk, entry_deadline AS "EntryDeadline",
                                   status, status_at AS "StatusAt", NULL::text AS reason, false AS "CancelRequested", false AS "CloseRequested",
                                   entry_quantity AS "EntryQuantity", entry_value AS "EntryValue", entry_fee AS "EntryFee",
                                   take_profit_quantity AS "TakeProfitQuantity", take_profit_value AS "TakeProfitValue", take_profit_fee AS "TakeProfitFee",
                                   stop_loss_quantity AS "StopLossQuantity", stop_loss_value AS "StopLossValue", stop_loss_fee AS "StopLossFee",
                                   0::numeric AS "ManualQuantity", 0::numeric AS "ManualValue", 0::numeric AS "ManualFee",
                                   expiry_quantity AS "ExpiryQuantity", expiry_value AS "ExpiryValue", expiry_fee AS "ExpiryFee",
                                   0 AS "LastOrderNumber", created_at AS "CreatedAt",
                                   promised_badge AS "PromisedBadge", promised_probability AS "PromisedProbability", promised_expectancy AS "PromisedExpectancy",
                                   promised_cases AS "PromisedCases", break_even AS "BreakEven", net_risk_reward AS "NetRiskReward",
                                   idea_status AS "IdeaStatus", idea_status_at AS "IdeaStatusAt", idea_result AS "IdeaResult", idea_exit_price AS "IdeaExitPrice", opened_at AS "OpenedAt",
                                   stepped_until AS "SteppedUntil"
                                   """;

    public static CommandDefinition GetOpen(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_paper_trades WHERE status IN ('awaiting_entry', 'open') ORDER BY created_at, id", cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetAll(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_paper_trades ORDER BY created_at DESC, id", cancellationToken: cancellationToken);
    }

    public static CommandDefinition Insert(IEnumerable<IdeaTradeDb> trades, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO idea_paper_trades (
                id, idea_id, auto_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk, entry_deadline,
                status, status_at, entry_quantity, entry_value, entry_fee, take_profit_quantity, take_profit_value, take_profit_fee,
                stop_loss_quantity, stop_loss_value, stop_loss_fee, created_at, opened_at,
                promised_badge, promised_probability, promised_expectancy, promised_cases, break_even, net_risk_reward, stepped_until)
            VALUES (
                @Id, @IdeaId, @AutoId, @AccountId, @Category, @Symbol, @Setup, @Interval, @Type, @Side, @EntryPrice, @Stop, @Target, @Quantity, @Amount, @Risk, @EntryDeadline,
                @Status, @StatusAt, @EntryQuantity, @EntryValue, @EntryFee, @TakeProfitQuantity, @TakeProfitValue, @TakeProfitFee,
                @StopLossQuantity, @StopLossValue, @StopLossFee, @CreatedAt, @OpenedAt,
                @PromisedBadge, @PromisedProbability, @PromisedExpectancy, @PromisedCases, @BreakEven, @NetRiskReward, @SteppedUntil)
            """,
            trades,
            transaction,
            cancellationToken: cancellationToken);
    }

    // Dapper runs the statement once per row; a step writes what the minute changed.
    public static CommandDefinition Update(IEnumerable<IdeaTradeDb> trades, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            UPDATE idea_paper_trades SET status = @Status, status_at = @StatusAt, entry_quantity = @EntryQuantity, entry_value = @EntryValue, entry_fee = @EntryFee,
                take_profit_quantity = @TakeProfitQuantity, take_profit_value = @TakeProfitValue, take_profit_fee = @TakeProfitFee,
                stop_loss_quantity = @StopLossQuantity, stop_loss_value = @StopLossValue, stop_loss_fee = @StopLossFee,
                expiry_quantity = @ExpiryQuantity, expiry_value = @ExpiryValue, expiry_fee = @ExpiryFee,
                opened_at = @OpenedAt, stepped_until = @SteppedUntil
            WHERE id = @Id
            """,
            trades,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition WriteOutcomes(IEnumerable<object> outcomes, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            UPDATE idea_paper_trades SET idea_status = @Status, idea_status_at = @StatusAt, idea_result = @Result, idea_exit_price = @ExitPrice
            WHERE idea_id = @IdeaId AND idea_status IS NULL
            """,
            outcomes,
            transaction,
            cancellationToken: cancellationToken);
    }
}