using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;

internal sealed class IdeaTradeRepository(NpgsqlDataSource dataSource, PostgresTransactionAccessor accessor) : IIdeaTradeRepository
{
    public async Task<IdeaTrade?> GetAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IdeaTradeDb? trade = await connection.QuerySingleOrDefaultAsync<IdeaTradeDb>(IdeaTradeRepositoryQueries.Get(tradeId, cancellationToken));

        return trade is null ? null : IdeaTradeDbConverter.ToDomain(trade);
    }

    public async Task<IReadOnlyList<IdeaTrade>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaTradeDb>(IdeaTradeRepositoryQueries.GetAll(cancellationToken))).Select(IdeaTradeDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<IdeaTrade>> GetOpenAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaTradeDb>(IdeaTradeRepositoryQueries.GetOpen(cancellationToken))).Select(IdeaTradeDbConverter.ToDomain)];
    }

    public async Task<LeasedIdeaTrade?> ClaimAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IdeaTradeDb? trade = await connection.QuerySingleOrDefaultAsync<IdeaTradeDb>(IdeaTradeRepositoryQueries.Claim(owner, now, leasedUntil, cancellationToken));

        return trade is null ? null : new LeasedIdeaTrade(IdeaTradeDbConverter.ToDomain(trade), trade.LeaseEpoch);
    }

    public async Task<IReadOnlyList<LeasedIdeaTrade>> RenewAllAsync(
        Guid owner,
        IReadOnlyCollection<(Guid TradeId, long Epoch)> trades,
        DateTimeOffset leasedUntil,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<IdeaTradeDb> renewed = await connection.QueryAsync<IdeaTradeDb>(
            IdeaTradeRepositoryQueries.RenewAll(owner, [.. trades.Select(trade => trade.TradeId)], [.. trades.Select(trade => trade.Epoch)], leasedUntil, cancellationToken));

        return [.. renewed.Select(trade => new LeasedIdeaTrade(IdeaTradeDbConverter.ToDomain(trade), trade.LeaseEpoch))];
    }

    public async Task<IReadOnlyList<ChangedIdeaTrade>> GetChangedAsync(Guid owner, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<IdeaTradeDb> changed = await connection.QueryAsync<IdeaTradeDb>(IdeaTradeRepositoryQueries.GetChanged(owner, since, cancellationToken));

        return [.. changed.Select(trade => new ChangedIdeaTrade(IdeaTradeDbConverter.ToDomain(trade), trade.LeaseEpoch, trade.UpdatedAt))];
    }

    public async Task ReleaseAsync(Guid tradeId, Guid owner, long epoch, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(IdeaTradeRepositoryQueries.Release(tradeId, owner, epoch, cancellationToken));
    }

    public async Task ReleaseAllAsync(Guid owner, IReadOnlyCollection<(Guid TradeId, long Epoch)> trades, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(IdeaTradeRepositoryQueries.ReleaseAll(owner, [.. trades.Select(trade => trade.TradeId)], [.. trades.Select(trade => trade.Epoch)], cancellationToken));
    }

    public async Task LockCreationAsync(CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.LockCreation(current.Transaction, cancellationToken));
    }

    public async Task CreateAsync(IdeaTrade trade, IdeaTradeOrder entry, DateTimeOffset now, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.Create(IdeaTradeDbConverter.FromDomain(trade), now, current.Transaction, cancellationToken));
        await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.SaveOrder(IdeaTradeDbConverter.FromDomain(entry), current.Transaction, cancellationToken));
    }

    public async Task<IdeaTrade?> LockAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        IdeaTradeDb? trade = await current.Connection.QuerySingleOrDefaultAsync<IdeaTradeDb>(IdeaTradeRepositoryQueries.SelectLocked(tradeId, current.Transaction, cancellationToken));

        return trade is null ? null : IdeaTradeDbConverter.ToDomain(trade);
    }

    public async Task<IdeaTrade?> LockLeasedAsync(Guid tradeId, Guid owner, long epoch, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        IdeaTradeDb? trade = await current.Connection.QuerySingleOrDefaultAsync<IdeaTradeDb>(IdeaTradeRepositoryQueries.LockLeased(tradeId, owner, epoch, current.Transaction, cancellationToken));

        return trade is null ? null : IdeaTradeDbConverter.ToDomain(trade);
    }

    public async Task SaveAsync(IdeaTrade trade, DateTimeOffset now, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.Save(IdeaTradeDbConverter.FromDomain(trade), now, current.Transaction, cancellationToken));
    }

    public async Task<IReadOnlyList<IdeaTradeOrder>> GetUnfinishedOrdersAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        IEnumerable<IdeaTradeOrderDb> orders = await current.Connection.QueryAsync<IdeaTradeOrderDb>(IdeaTradeRepositoryQueries.GetUnfinishedOrders(tradeId, current.Transaction, cancellationToken));

        return [.. orders.Select(IdeaTradeDbConverter.ToDomain)];
    }

    public async Task SaveOrderAsync(IdeaTradeOrder order, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.SaveOrder(IdeaTradeDbConverter.FromDomain(order), current.Transaction, cancellationToken));
    }

    public async Task SaveChildAsync(IdeaTradeChild child, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.SaveChild(IdeaTradeDbConverter.FromDomain(child), current.Transaction, cancellationToken));
    }

    public async Task<bool> AddFillAsync(IdeaTradeFill fill, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;

        return await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.AddFill(IdeaTradeDbConverter.FromDomain(fill), current.Transaction, cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<IdeaTrade>> LockHoldersAsync(Guid accountId, Instrument instrument, IdeaSide side, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        IEnumerable<IdeaTradeDb> trades = await current.Connection.QueryAsync<IdeaTradeDb>(IdeaTradeRepositoryQueries.LockHolders(accountId, instrument, side, current.Transaction, cancellationToken));

        return [.. trades.Select(IdeaTradeDbConverter.ToDomain)];
    }

    public async Task<bool> HasForcedSharesAsync(string execId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;

        return await current.Connection.ExecuteScalarAsync<bool>(IdeaTradeRepositoryQueries.HasForcedShares(execId, current.Transaction, cancellationToken));
    }

    public async Task AddForcedSharesAsync(IReadOnlyList<IdeaTradeForcedShare> shares, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;

        foreach (IdeaTradeForcedShare share in shares)
        {
            await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.AddForcedShare(IdeaTradeDbConverter.FromDomain(share), current.Transaction, cancellationToken));
        }
    }

    public async Task<IdeaTradeForcedShare?> GetUnappliedForcedShareAsync(string execId, Guid tradeId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        IdeaTradeForcedShareDb? share = await current.Connection.QuerySingleOrDefaultAsync<IdeaTradeForcedShareDb>(
            IdeaTradeRepositoryQueries.GetUnappliedForcedShare(execId, tradeId, current.Transaction, cancellationToken));

        return share is null ? null : IdeaTradeDbConverter.ToDomain(share);
    }

    public async Task MarkForcedShareAppliedAsync(string execId, Guid tradeId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(IdeaTradeRepositoryQueries.MarkForcedShareApplied(execId, tradeId, current.Transaction, cancellationToken));
    }

    public async Task<bool> IsChildAsync(string exchangeOrderId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(IdeaTradeRepositoryQueries.IsChild(exchangeOrderId, cancellationToken));
    }

    public async Task<IReadOnlyList<IdeaTradeOrder>> GetOrdersAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaTradeOrderDb>(IdeaTradeRepositoryQueries.GetOrders(tradeId, cancellationToken))).Select(IdeaTradeDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<IdeaTradeChild>> GetChildrenAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaTradeChildDb>(IdeaTradeRepositoryQueries.GetChildren(tradeId, cancellationToken))).Select(IdeaTradeDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<IdeaTradeFill>> GetFillsAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaTradeFillDb>(IdeaTradeRepositoryQueries.GetFills(tradeId, cancellationToken))).Select(IdeaTradeDbConverter.ToDomain)];
    }
}