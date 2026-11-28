using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     The trades in <c>trades</c>, <c>trade_orders</c> and <c>trade_fills</c> (change take-idea-trades, design.md,
///     decisions 1, 2, 7 and 12). The entry writes the trade with its order under the advisory lock of the entry, the limits
///     counted anew under it; a change reads the trade under its lock and writes it back whole; a change of the trading Worker
///     first passes the fence of its lease.
/// </summary>
internal sealed class TradeRepository(NpgsqlDataSource database) : ITradeRepository
{
    private const string UniqueViolation = "23505";
    private const string HoldingIndex = "trades_holding_ux";

    public async Task<EntryRefusal?> OpenAsync(Trade trade, TradeOrder entry, int maxOpenTrades, decimal maxTotalAmount, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(@Key)", new { Key = TraderFence.EntryLock }, transaction, cancellationToken: cancellationToken));
        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM trade_halt WHERE id = 1 AND halted_at IS NOT NULL)", transaction: transaction, cancellationToken: cancellationToken)))
        {
            return new EntryRefusal(new EntryCheck("halt", false, "Вход запрещён стоп-краном"));
        }

        (int open, decimal amount, bool taken) = await Counters(connection, transaction, trade.AccountId, trade.Symbol, cancellationToken);
        if (taken)
        {
            return new EntryRefusal(new EntryCheck("pairTrade", false, "По паре уже есть сделка"));
        }

        if (open >= maxOpenTrades)
        {
            return new EntryRefusal(new EntryCheck("open", false, $"Открыто {open} сделок из {maxOpenTrades}"));
        }

        if (amount + trade.Amount > maxTotalAmount)
        {
            return new EntryRefusal(new EntryCheck("total", false, $"В сделках было бы {TradeTexts.Money(amount + trade.Amount)} USDT при пределе {TradeTexts.Money(maxTotalAmount)}"));
        }

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(TradeRows.InsertTrade, TradeRows.Insert(trade), transaction, cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation && exception.ConstraintName == HoldingIndex)
        {
            return new EntryRefusal(new EntryCheck("pairTrade", false, "По паре уже есть сделка"));
        }

        await connection.ExecuteAsync(new CommandDefinition(TradeRows.UpsertOrder, TradeRows.Of(entry), transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public async Task<(int Open, decimal Amount, bool PairTaken)> CountersAsync(Guid? accountId, string symbol, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await Counters(connection, null, accountId, symbol, cancellationToken);
    }

    public async Task<Trade?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        TradeRows.TradeRow? row = await connection.QuerySingleOrDefaultAsync<TradeRows.TradeRow>(new CommandDefinition(
            $"SELECT {TradeRows.TradeColumns} FROM trades t WHERE t.id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<TradeBook>> HoldingAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        List<Trade> trades = [.. (await connection.QueryAsync<TradeRows.TradeRow>(new CommandDefinition(
            $"SELECT {TradeRows.TradeColumns} FROM trades t WHERE t.state IN {TradeRows.Holding} ORDER BY t.created_at, t.id",
            cancellationToken: cancellationToken))).Select(row => row.ToDomain())];
        ILookup<Guid, TradeOrder> orders = (await connection.QueryAsync<TradeRows.OrderRow>(new CommandDefinition(
                $"SELECT {TradeRows.OrderColumns} FROM trade_orders WHERE trade_id = ANY(@Ids) ORDER BY trade_id, number",
                new { Ids = trades.Select(trade => trade.Id).ToArray() },
                cancellationToken: cancellationToken)))
            .Select(row => row.ToDomain())
            .ToLookup(order => order.TradeId);
        return [.. trades.Select(trade => new TradeBook(trade, [.. orders[trade.Id]], [], new HashSet<string>()))];
    }

    public async Task<IReadOnlyList<TradeFill>> FillsAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<TradeRows.FillRow>(new CommandDefinition(
            $"SELECT {TradeRows.FillColumns} FROM trade_fills WHERE trade_id = @Id ORDER BY time, exec_id",
            new { Id = tradeId },
            cancellationToken: cancellationToken))).Select(row => row.ToDomain())];
    }

    public async Task<TradeBook?> ChangeAsync(Guid id, string? worker, Func<TradeBook, bool> change, IReadOnlyCollection<string>? probe, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await TraderFence.CheckAsync(connection, transaction, worker, cancellationToken);
        TradeRows.TradeRow? row = await connection.QuerySingleOrDefaultAsync<TradeRows.TradeRow>(new CommandDefinition(
            $"SELECT {TradeRows.TradeColumns} FROM trades t WHERE t.id = @Id FOR UPDATE",
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        List<TradeOrder> orders = [.. (await connection.QueryAsync<TradeRows.OrderRow>(new CommandDefinition(
            $"SELECT {TradeRows.OrderColumns} FROM trade_orders WHERE trade_id = @Id ORDER BY number",
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken))).Select(order => order.ToDomain())];
        List<TradeFill> fills = [.. (await connection.QueryAsync<TradeRows.FillRow>(new CommandDefinition(
            $"SELECT {TradeRows.FillColumns} FROM trade_fills WHERE trade_id = @Id ORDER BY time, exec_id",
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken))).Select(fill => fill.ToDomain())];
        HashSet<string> known = probe is { Count: > 0 }
            ? [.. await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT exec_id FROM trade_fills WHERE exec_id = ANY(@Ids)",
                new { Ids = probe.ToArray() },
                transaction,
                cancellationToken: cancellationToken))]
            : [];
        HashSet<string> before = [.. fills.Select(fill => fill.ExecId)];
        var book = new TradeBook(row.ToDomain(), orders, fills, known);
        if (!change(book))
        {
            return book;
        }

        await connection.ExecuteAsync(new CommandDefinition(TradeRows.UpdateTrade, TradeRows.Update(book.Trade), transaction, cancellationToken: cancellationToken));
        if (book.Orders.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(TradeRows.UpsertOrder, book.Orders.Select(TradeRows.Of).ToArray(), transaction, cancellationToken: cancellationToken));
        }

        TradeFill[] added = [.. book.Fills.Where(fill => !before.Contains(fill.ExecId))];
        if (added.Length > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(TradeRows.InsertFill, added.Select(TradeRows.Of).ToArray(), transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return book;
    }

    public async Task MarkAsync(string worker, IReadOnlyDictionary<Guid, decimal> unrealised, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await TraderFence.CheckAsync(connection, transaction, worker, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE trades SET unrealised = @Unrealised, marked_at = @At WHERE id = @Id AND state IN {TradeRows.Holding}",
            unrealised.Select(pair => new { Id = pair.Key, Unrealised = pair.Value, At = at }).ToArray(),
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, (Guid TradeId, TradeState State)>> ByIdeasAsync(IReadOnlyCollection<Guid> ideaIds, CancellationToken cancellationToken)
    {
        if (ideaIds.Count == 0)
        {
            return new Dictionary<Guid, (Guid, TradeState)>();
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<(Guid IdeaId, Guid Id, string State)> rows = await connection.QueryAsync<(Guid, Guid, string)>(new CommandDefinition(
            "SELECT DISTINCT ON (idea_id) idea_id, id, state FROM trades WHERE idea_id = ANY(@Ids) AND state <> 'cancelled' ORDER BY idea_id, created_at DESC",
            new { Ids = ideaIds.ToArray() },
            cancellationToken: cancellationToken));
        return rows.ToDictionary(row => row.IdeaId, row => (row.Id, TradeNames.State(row.State)));
    }

    public async Task<ListPage<TradeListItem>> ListAsync(TradeListQuery query, CancellationToken cancellationToken)
    {
        string where = query.Open ? $"t.state IN {TradeRows.Holding}" : "t.state IN ('closed', 'cancelled')";
        string order = $"{Column(query.Order.Sort)} {(query.Order.Descending ? "DESC" : "ASC")} NULLS LAST, t.created_at DESC, t.id";
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        int total = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*) FROM trades t WHERE {where}", cancellationToken: cancellationToken));
        IEnumerable<TradeRows.TradeRow> rows = await connection.QueryAsync<TradeRows.TradeRow>(new CommandDefinition(
            $"""
             SELECT {TradeRows.TradeColumns}, i.result AS "IdeaResult"
             FROM trades t LEFT JOIN ideas i ON i.id = t.idea_id AND i.state = 'closed'
             WHERE {where}
             ORDER BY {order}
             LIMIT @Limit OFFSET @Offset
             """,
            new { Limit = query.Order.PageSize, query.Order.Offset },
            cancellationToken: cancellationToken));
        return new ListPage<TradeListItem>([.. rows.Select(row => new TradeListItem(row.ToDomain(), row.IdeaResult))], total, query.Order.Page, query.Order.PageSize);
    }

    public async Task<(int Open, int Closed)> CountsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<(int, int)>(new CommandDefinition(
            $"SELECT count(*) FILTER (WHERE state IN {TradeRows.Holding})::int, count(*) FILTER (WHERE state IN ('closed', 'cancelled'))::int FROM trades",
            cancellationToken: cancellationToken));
    }

    public async Task<(IReadOnlyList<(DateTimeOffset ClosedAt, decimal Result)> Closed, IReadOnlyList<decimal> Open)> DayAsync(DateTimeOffset from, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<(DateTimeOffset, decimal)> closed = await connection.QueryAsync<(DateTimeOffset, decimal)>(new CommandDefinition(
            "SELECT closed_at, result FROM trades WHERE state = 'closed' AND result IS NOT NULL AND closed_at >= @From",
            new { From = from },
            cancellationToken: cancellationToken));
        IEnumerable<decimal> open = await connection.QueryAsync<decimal>(new CommandDefinition(
            $"SELECT unrealised FROM trades WHERE state IN {TradeRows.Holding} AND unrealised IS NOT NULL",
            cancellationToken: cancellationToken));
        return ([.. closed], [.. open]);
    }

    public async Task<bool> HoldsAnyAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"SELECT EXISTS (SELECT 1 FROM trades WHERE account_id = @Id AND state IN {TradeRows.Holding})",
            new { Id = accountId },
            cancellationToken: cancellationToken));
    }

    private static async Task<(int Open, decimal Amount, bool PairTaken)> Counters(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid? accountId, string symbol, CancellationToken cancellationToken)
    {
        return await connection.QuerySingleAsync<(int, decimal, bool)>(new CommandDefinition(
            $"""
             SELECT count(*)::int, coalesce(sum(amount), 0), coalesce(bool_or(account_id = @Account AND symbol = @Symbol), false)
             FROM trades WHERE state IN {TradeRows.Holding}
             """,
            new { Account = accountId, Symbol = symbol },
            transaction,
            cancellationToken: cancellationToken));
    }

    // The column of a field of ListFields.Trades.
    private static string Column(string field)
    {
        return field switch
        {
            "symbol" => "t.symbol",
            "account" => "t.account_name",
            "strategy" => "t.strategy_name",
            "side" => "t.side",
            "amount" => "t.amount",
            "created" => "t.created_at",
            "entryTime" => "t.opened_at",
            "entry" => "t.entry_price",
            "stop" => "t.stop",
            "target" => "t.target",
            "left" => "t.until",
            "now" => "t.unrealised",
            "state" => "t.state",
            "exitTime" => "t.closed_at",
            "exitReason" => "coalesce(t.close_reason, t.state)",
            "fees" => "t.fees",
            "result" => "t.result",
            "share" => "t.result / t.amount",
            "ideaResult" => "i.result",
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a field of the list of trades.")
        };
    }
}
