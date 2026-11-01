using Dapper;

using Npgsql;

using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

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

    public async Task<IReadOnlyList<Backtest>> GetUnfinishedAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<BacktestDb>(BacktestRepositoryQueries.GetUnfinished(engineVersion, cancellationToken))).Select(BacktestDbConverter.ToDomain)];
    }

    public async Task FailOutdatedAsync(int engineVersion, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(BacktestRepositoryQueries.FailOutdated(engineVersion, reason, now, cancellationToken));
    }

    public async Task<bool> UpdateAsync(Backtest backtest, BacktestStatus expected, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(BacktestRepositoryQueries.Update(BacktestDbConverter.FromDomain(backtest), expected.ToCode(), null, cancellationToken)) > 0;
    }

    public async Task<bool> UpdateStatusAsync(
        Guid id,
        IReadOnlyCollection<BacktestStatus> expected,
        BacktestStatus status,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        CommandDefinition update = BacktestRepositoryQueries.UpdateStatus(id, expected.Select(code => code.ToCode()), status.ToCode(), finishedAt, cancellationToken);

        return await connection.ExecuteAsync(update) > 0;
    }

    public async Task<bool> FailAsync(Guid id, IReadOnlyCollection<BacktestStatus> expected, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(BacktestRepositoryQueries.Fail(id, expected.Select(status => status.ToCode()), reason, now, cancellationToken)) > 0;
    }

    public async Task<bool> StartImportAsync(Backtest backtest, CandleImportJob job, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await connection.ExecuteAsync(BacktestRepositoryQueries.StartImport(BacktestDbConverter.FromDomain(backtest), transaction, cancellationToken)) == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(CandleImportJobRepositoryQueries.Insert(CandleImportJobDbConverter.FromDomain(job), cancellationToken, transaction));
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<bool> StartRunningAsync(Backtest backtest, IReadOnlyList<BacktestChunk> chunks, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        CommandDefinition update = BacktestRepositoryQueries.Update(
            BacktestDbConverter.FromDomain(backtest),
            BacktestStatus.LoadingCandles.ToCode(),
            transaction,
            cancellationToken);

        if (await connection.ExecuteAsync(update) == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(BacktestRepositoryQueries.InsertChunks(backtest.Id, chunks, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<BacktestChunk?> ClaimChunkAsync(Guid backtestId, Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        BacktestChunkDb? chunk = await connection.QuerySingleOrDefaultAsync<BacktestChunkDb>(
            BacktestRepositoryQueries.ClaimChunk(backtestId, owner, now, leasedUntil, cancellationToken));

        return chunk is null ? null : new BacktestChunk(chunk.BacktestId, chunk.Number, chunk.FirstCombination, chunk.Combinations, chunk.LostLeases);
    }

    public async Task<bool> RenewChunkLeaseAsync(BacktestChunk chunk, Guid owner, DateTimeOffset leasedUntil, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(BacktestRepositoryQueries.RenewChunkLease(chunk, owner, leasedUntil, cancellationToken)) > 0;
    }

    public async Task<bool> SaveChunkProgressAsync(
        BacktestChunk chunk,
        Guid owner,
        IReadOnlyList<BacktestResult> results,
        decimal? speed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await connection.QuerySingleOrDefaultAsync<Guid?>(BacktestRepositoryQueries.LockRunning(chunk.BacktestId, transaction, cancellationToken)) is null
            || await connection.ExecuteAsync(BacktestRepositoryQueries.SaveChunk(chunk, owner, speed, transaction, cancellationToken)) == 0)
        {
            return false;
        }

        var inserted = 0;

        foreach (BacktestResult result in results)
        {
            inserted += await connection.ExecuteAsync(BacktestRepositoryQueries.InsertResult(BacktestResultDbConverter.FromDomain(result), transaction, cancellationToken));
        }

        await connection.ExecuteAsync(BacktestRepositoryQueries.CountResultsOfChunk(chunk.BacktestId, inserted, now, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<bool> CompleteChunkAsync(BacktestChunk chunk, Guid owner, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(BacktestRepositoryQueries.CompleteChunk(chunk, owner, cancellationToken)) > 0;
    }

    public async Task<bool> SucceedIfCompleteAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(BacktestRepositoryQueries.SucceedIfComplete(id, now, cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<long>> GetCalculatedCombinationsAsync(BacktestChunk chunk, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. await connection.QueryAsync<long>(BacktestRepositoryQueries.CalculatedCombinations(chunk, cancellationToken))];
    }

    public async Task<IReadOnlyList<long>> GetCalculatedCombinationsAsync(Guid backtestId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. await connection.QueryAsync<long>(BacktestRepositoryQueries.CalculatedCombinations(backtestId, cancellationToken))];
    }

    public async Task<IReadOnlyList<BacktestResult>> GetResultsAsync(
        Guid backtestId,
        ResultMetric sort,
        bool descending,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<BacktestResultDb> results =
            await connection.QueryAsync<BacktestResultDb>(BacktestRepositoryQueries.Results(backtestId, sort, descending, offset, limit, cancellationToken));

        return [.. results.Select(BacktestResultDbConverter.ToDomain)];
    }

    public async Task<long> CountResultsAsync(Guid backtestId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<long>(BacktestRepositoryQueries.CountResults(backtestId, cancellationToken));
    }

    public async Task<BacktestResult?> GetResultAsync(Guid backtestId, long combination, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        BacktestResultDb? result = await connection.QuerySingleOrDefaultAsync<BacktestResultDb>(BacktestRepositoryQueries.Result(backtestId, combination, cancellationToken));

        return result is null ? null : BacktestResultDbConverter.ToDomain(result);
    }
}