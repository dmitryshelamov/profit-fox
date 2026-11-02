using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading;

internal sealed class LiveBotRepository(NpgsqlDataSource dataSource, PostgresTransactionAccessor accessor) : ILiveBotRepository
{
    public async Task<LiveBot> GetAsync(Guid botId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        LiveBotDb? bot = await connection.QuerySingleOrDefaultAsync<LiveBotDb>(LiveBotRepositoryQueries.Get(botId, cancellationToken));

        return bot is null ? LiveBot.New(botId) : LiveTradingDbConverter.ToDomain(bot);
    }

    public async Task<IReadOnlyDictionary<Guid, LiveBot>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return (await connection.QueryAsync<LiveBotDb>(LiveBotRepositoryQueries.GetAll(cancellationToken))).Select(LiveTradingDbConverter.ToDomain).ToDictionary(bot => bot.BotId);
    }

    public async Task<LeasedBot?> ClaimAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        LiveBotDb? bot = await connection.QuerySingleOrDefaultAsync<LiveBotDb>(LiveBotRepositoryQueries.Claim(owner, now, leasedUntil, cancellationToken));

        return bot is null ? null : new LeasedBot(LiveTradingDbConverter.ToDomain(bot), bot.LeaseEpoch);
    }

    public async Task<LiveBot?> RenewAsync(Guid botId, Guid owner, long epoch, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        LiveBotDb? bot = await connection.QuerySingleOrDefaultAsync<LiveBotDb>(LiveBotRepositoryQueries.Renew(botId, owner, epoch, leasedUntil, cancellationToken));

        return bot is null ? null : LiveTradingDbConverter.ToDomain(bot);
    }

    public async Task ReleaseAsync(Guid botId, Guid owner, long epoch, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(LiveBotRepositoryQueries.Release(botId, owner, epoch, cancellationToken));
    }

    public async Task<LiveBot?> LockAsync(Guid botId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;

        if (await current.Connection.QuerySingleOrDefaultAsync<Guid?>(LiveBotRepositoryQueries.Lock(botId, now, current.Transaction, cancellationToken)) is null)
        {
            return null;
        }

        LiveBotDb bot = await current.Connection.QuerySingleAsync<LiveBotDb>(LiveBotRepositoryQueries.SelectLocked(botId, current.Transaction, cancellationToken));

        return LiveTradingDbConverter.ToDomain(bot);
    }

    public async Task LockAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(LiveBotRepositoryQueries.LockAccount(accountId, current.Transaction, cancellationToken));
    }

    public async Task<LiveBot?> LockLeasedAsync(Guid botId, Guid owner, long epoch, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        LiveBotDb? bot = await current.Connection.QuerySingleOrDefaultAsync<LiveBotDb>(
            LiveBotRepositoryQueries.LockLeased(botId, owner, epoch, current.Transaction, cancellationToken));

        return bot is null ? null : LiveTradingDbConverter.ToDomain(bot);
    }

    public async Task SaveAsync(LiveBot bot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(LiveBotRepositoryQueries.Save(LiveTradingDbConverter.FromDomain(bot), now, current.Transaction, cancellationToken));
    }

    public async Task<LivePosition?> GetCurrentPositionAsync(Guid botId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        LivePositionDb? position = await current.Connection.QuerySingleOrDefaultAsync<LivePositionDb>(
            LiveBotRepositoryQueries.GetCurrentPosition(botId, current.Transaction, cancellationToken));

        return position is null ? null : LiveTradingDbConverter.ToDomain(position);
    }

    public async Task<decimal> GetPastPnlAsync(Guid botId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;

        return await current.Connection.ExecuteScalarAsync<decimal>(LiveBotRepositoryQueries.GetPastPnl(botId, current.Transaction, cancellationToken));
    }

    public async Task SavePositionAsync(LivePosition position, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(LiveBotRepositoryQueries.SavePosition(LiveTradingDbConverter.FromDomain(position), current.Transaction, cancellationToken));
    }

    public async Task<IReadOnlyList<LiveOrder>> GetUnfinishedOrdersAsync(Guid botId, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        IEnumerable<LiveOrderDb> orders = await current.Connection.QueryAsync<LiveOrderDb>(
            LiveBotRepositoryQueries.GetUnfinishedOrders(botId, current.Transaction, cancellationToken));

        return [.. orders.Select(LiveTradingDbConverter.ToDomain)];
    }

    public async Task SaveOrderAsync(LiveOrder order, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(LiveBotRepositoryQueries.SaveOrder(LiveTradingDbConverter.FromDomain(order), current.Transaction, cancellationToken));
    }

    public async Task<bool> AddFillAsync(LiveFill fill, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;

        return await current.Connection.ExecuteAsync(LiveBotRepositoryQueries.AddFill(LiveTradingDbConverter.FromDomain(fill), current.Transaction, cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<LivePosition>> GetPositionsAsync(Guid botId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<LivePositionDb>(LiveBotRepositoryQueries.GetPositions(botId, cancellationToken))).Select(LiveTradingDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<LiveOrder>> GetOrdersAsync(Guid botId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<LiveOrderDb>(LiveBotRepositoryQueries.GetOrders(botId, cancellationToken))).Select(LiveTradingDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<LiveFill>> GetFillsAsync(Guid botId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<LiveFillDb>(LiveBotRepositoryQueries.GetFills(botId, cancellationToken))).Select(LiveTradingDbConverter.ToDomain)];
    }
}