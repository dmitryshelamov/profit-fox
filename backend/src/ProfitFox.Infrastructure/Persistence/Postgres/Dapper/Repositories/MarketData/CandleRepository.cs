using Dapper;

using Npgsql;

using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;

internal sealed class CandleRepository(NpgsqlDataSource dataSource) : ICandleRepository
{
    public async Task UpsertAsync(Instrument instrument, Interval interval, IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken)
    {
        var dbCandles = candles.Select(candle => CandleDbConverter.FromDomain(instrument, interval, candle)).ToList();
        await using NpgsqlCommand command = CandleRepositoryQueries.Upsert(dataSource, dbCandles);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(Instrument instrument, Interval interval, IReadOnlyCollection<Candle> candles, SeriesCoverage coverage, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (candles.Count > 0)
        {
            var dbCandles = candles.Select(candle => CandleDbConverter.FromDomain(instrument, interval, candle)).ToList();
            await using NpgsqlCommand command = CandleRepositoryQueries.Upsert(connection, dbCandles);
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await connection.ExecuteAsync(
            SeriesCoverageRepositoryQueries.Upsert(instrument.Category.ToCode(), instrument.Symbol, interval.ToCode(), coverage.From, coverage.Until, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertAsync(Interval interval, IReadOnlyCollection<(Instrument Instrument, Candle Candle)> candles, CancellationToken cancellationToken)
    {
        // One statement cannot write a row twice, so the later of two candles with the same key wins.
        var dbCandles = candles
            .Select(item => CandleDbConverter.FromDomain(item.Instrument, interval, item.Candle))
            .GroupBy(candle => (candle.Category, candle.Symbol, candle.OpenTime))
            .Select(same => same.Last())
            .ToList();
        await using NpgsqlCommand command = CandleRepositoryQueries.Upsert(dataSource, dbCandles);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Candle>> GetAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<CandleDb> candles = await connection.QueryAsync<CandleDb>(
            CandleRepositoryQueries.Get(instrument.Category.ToCode(), instrument.Symbol, interval.ToCode(), from, to, limit, cancellationToken));

        return candles.Select(CandleDbConverter.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<DateTimeOffset>> GetOpenTimesAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        // Npgsql reads timestamptz as a UTC DateTime; Dapper converts it to DateTimeOffset only for members, not for scalars.
        IEnumerable<DateTime> openTimes = await connection.QueryAsync<DateTime>(
            CandleRepositoryQueries.GetOpenTimes(instrument.Category.ToCode(), instrument.Symbol, interval.ToCode(), from, to, cancellationToken));

        return openTimes.Select(openTime => new DateTimeOffset(openTime)).ToList();
    }

    public async Task<IReadOnlyList<IdeaCandle>> GetAggregatedAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        string category = instrument.Category.ToCode();
        var minutes = (int)(interval.CandleEnd(from) - from).TotalMinutes;
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<MinuteAggregateDb> rows = await connection.QueryAsync<MinuteAggregateDb>(
            CandleRepositoryQueries.GetAggregated(category, instrument.Symbol, minutes, from, to, cancellationToken));
        decimal? closeBefore = await connection.ExecuteScalarAsync<decimal?>(CandleRepositoryQueries.GetCloseBefore(category, instrument.Symbol, from, cancellationToken));
        MinuteAggregate[] aggregates = [.. rows.Select(row => new MinuteAggregate(row.OpenTime, row.FirstMinute, row.Open, row.High, row.Low, row.Close, row.Volume))];

        return IdeaCandles.Join(interval, aggregates, from, to, closeBefore);
    }
}