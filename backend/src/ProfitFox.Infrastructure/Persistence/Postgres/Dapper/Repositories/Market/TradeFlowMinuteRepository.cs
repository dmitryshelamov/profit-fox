using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

/// <summary>Writes under the lease of the trade flow, checked in the transaction of the write (decision 2 of collect-trade-flow).</summary>
internal sealed class TradeFlowMinuteRepository(NpgsqlDataSource dataSource) : ITradeFlowMinuteRepository
{
    public async Task<bool> SaveAsync(Guid owner, IReadOnlyCollection<(Instrument Instrument, TradeFlowMinute Minute)> minutes, CancellationToken cancellationToken)
    {
        return await UnderLeaseAsync(
            owner,
            async (connection, transaction) =>
            {
                await using NpgsqlCommand upsert = TradeFlowMinuteRepositoryQueries.Upsert(
                    connection,
                    transaction,
                    [.. minutes.Select(item => (item.Instrument.Category.ToCode(), item.Instrument.Symbol, item.Minute))]);
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    public async Task<bool> ReplaceDayAsync(Guid owner, Instrument instrument, DateTimeOffset day, IReadOnlyCollection<TradeFlowMinute> minutes, CancellationToken cancellationToken)
    {
        string category = instrument.Category.ToCode();

        return await UnderLeaseAsync(
            owner,
            async (connection, transaction) =>
            {
                await connection.ExecuteAsync(TradeFlowMinuteRepositoryQueries.DeleteDay(category, instrument.Symbol, day, transaction, cancellationToken));
                await using NpgsqlCommand insert = TradeFlowMinuteRepositoryQueries.Upsert(
                    connection,
                    transaction,
                    [.. minutes.Select(minute => (category, instrument.Symbol, minute))]);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<TradeFlowMinute>> GetBarsAsync(Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        DateTimeOffset open = interval.CandleOpen(from);
        var minutes = (int)(interval.CandleEnd(open) - open).TotalMinutes;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<TradeFlowMinuteDb> rows = await connection.QueryAsync<TradeFlowMinuteDb>(
            TradeFlowMinuteRepositoryQueries.GetBars(instrument.Category.ToCode(), instrument.Symbol, minutes, from, to, cancellationToken));

        return [.. rows.Select(TradeFlowMinuteDbConverter.ToDomain)];
    }

    public async Task<DateTimeOffset?> FirstAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = TradeFlowMinuteRepositoryQueries.First(dataSource, instrument.Category.ToCode(), instrument.Symbol);

        return await ReadTimeAsync(command, cancellationToken);
    }

    public async Task<DateTimeOffset?> LastAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = TradeFlowMinuteRepositoryQueries.Last(dataSource);

        return await ReadTimeAsync(command, cancellationToken);
    }

    // Npgsql reads timestamptz as a UTC DateTimeOffset only when asked for one; Dapper would get a DateTime.
    private static async Task<DateTimeOffset?> ReadTimeAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(0);
    }

    private async Task<bool> UnderLeaseAsync(Guid owner, Func<NpgsqlConnection, NpgsqlTransaction, Task> write, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await connection.ExecuteScalarAsync<int?>(TradeFlowMinuteRepositoryQueries.LockLease(owner, transaction, cancellationToken)) is null)
        {
            return false;
        }

        await write(connection, transaction);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}