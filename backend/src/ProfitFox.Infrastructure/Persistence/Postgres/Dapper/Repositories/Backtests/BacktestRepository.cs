using Dapper;

using Npgsql;

using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;

internal sealed class BacktestRepository(NpgsqlDataSource dataSource) : IBacktestRepository
{
    public async Task AddAsync(Backtest backtest, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(BacktestRepositoryQueries.Insert(BacktestDbConverter.FromDomain(backtest), cancellationToken));
    }

    public async Task<Backtest?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        BacktestDb? backtest = await connection.QuerySingleOrDefaultAsync<BacktestDb>(BacktestRepositoryQueries.Get(id, cancellationToken));

        return backtest is null ? null : BacktestDbConverter.ToDomain(backtest);
    }

    public async Task<IReadOnlyList<Backtest>> GetByBotAsync(Guid botId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<BacktestDb>(BacktestRepositoryQueries.GetByBot(botId, cancellationToken))).Select(BacktestDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<Backtest>> GetUnfinishedAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<BacktestDb>(BacktestRepositoryQueries.GetUnfinished(cancellationToken))).Select(BacktestDbConverter.ToDomain)];
    }

    public async Task<bool> UpdateAsync(Backtest backtest, BacktestStatus expected, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(BacktestRepositoryQueries.Update(BacktestDbConverter.FromDomain(backtest), expected.ToCode(), null, cancellationToken)) > 0;
    }

    public async Task<bool> SaveProgressAsync(Backtest backtest, IReadOnlyList<BacktestResult> results, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        CommandDefinition update = BacktestRepositoryQueries.Update(BacktestDbConverter.FromDomain(backtest), BacktestStatus.Running.ToCode(), transaction, cancellationToken);

        if (await connection.ExecuteAsync(update) == 0)
        {
            return false;
        }

        foreach (BacktestResult result in results)
        {
            await connection.ExecuteAsync(BacktestRepositoryQueries.InsertResult(BacktestResultDbConverter.FromDomain(result), transaction, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<long>> GetCalculatedCombinationsAsync(Guid backtestId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. await connection.QueryAsync<long>(BacktestRepositoryQueries.CalculatedCombinations(backtestId, cancellationToken))];
    }

    public async Task<BacktestResult?> GetResultAsync(Guid backtestId, long combination, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        BacktestResultDb? result = await connection.QuerySingleOrDefaultAsync<BacktestResultDb>(BacktestRepositoryQueries.Result(backtestId, combination, cancellationToken));

        return result is null ? null : BacktestResultDbConverter.ToDomain(result);
    }
}