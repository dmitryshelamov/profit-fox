using Dapper;

using Npgsql;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

internal sealed class IdeaBucketRepository(NpgsqlDataSource dataSource) : IIdeaBucketRepository
{
    private const string Columns = """
                                   engine_version AS "EngineVersion", days, setup, type, "interval", side, category, cases, expired, probability,
                                   probability_low AS "ProbabilityLow", probability_high AS "ProbabilityHigh", baseline, expectancy
                                   """;

    public async Task ReplaceAsync(int engineVersion, IReadOnlyCollection<IdeaBucket> buckets, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM idea_buckets", transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO idea_buckets (engine_version, days, setup, type, "interval", side, category, cases, expired, probability, probability_low,
                    probability_high, baseline, expectancy)
                VALUES (@EngineVersion, @Days, @Setup, @Type, @Interval, @Side, @Category, @Cases, @Expired, @Probability, @ProbabilityLow,
                    @ProbabilityHigh, @Baseline, @Expectancy)
                """,
                buckets.Select(bucket => Row(engineVersion, bucket)),
                transaction,
                cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IdeaBucket>> GetAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<IdeaBucketDb> rows = await connection.QueryAsync<IdeaBucketDb>(
            new CommandDefinition($"SELECT {Columns} FROM idea_buckets WHERE engine_version = @Version", new { Version = engineVersion }, cancellationToken: cancellationToken));

        return [.. rows.Select(Bucket)];
    }

    private static IdeaBucketDb Row(int engineVersion, IdeaBucket bucket)
    {
        BucketStatistics statistics = bucket.Statistics;

        return new IdeaBucketDb
        {
            EngineVersion = engineVersion,
            Days = bucket.Days,
            Setup = bucket.Key.Setup.ToCode(),
            Type = bucket.Key.Type.ToCode(),
            Interval = bucket.Key.Interval.ToCode(),
            Side = bucket.Key.Side.ToCode(),
            Category = bucket.Key.Category.ToCode(),
            Cases = statistics.Cases,
            Expired = statistics.Expired,
            Probability = statistics.Probability,
            ProbabilityLow = statistics.ProbabilityLow,
            ProbabilityHigh = statistics.ProbabilityHigh,
            Baseline = statistics.Baseline,
            Expectancy = statistics.Expectancy
        };
    }

    private static IdeaBucket Bucket(IdeaBucketDb row)
    {
        IdeaCodes.TryParseSetup(row.Setup, out IdeaSetup setup);
        IdeaCodes.TryParseType(row.Type, out IdeaType type);
        IdeaCodes.TryParseSide(row.Side, out IdeaSide side);
        MarketDataCodes.TryParseInterval(row.Interval, out Interval interval);
        MarketDataCodes.TryParseCategory(row.Category, out MarketCategory category);

        return new IdeaBucket(
            new IdeaBucketKey(setup, type, interval, side, category),
            row.Days,
            new BucketStatistics(row.Cases, row.Expired, row.Probability, row.ProbabilityLow, row.ProbabilityHigh, row.Baseline, row.Expectancy));
    }
}