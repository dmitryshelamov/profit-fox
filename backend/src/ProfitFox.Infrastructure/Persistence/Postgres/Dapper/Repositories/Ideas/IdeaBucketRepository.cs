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
                                   engine_version AS "EngineVersion", days, setup, type, "interval", side, category, cases, targets, stops, expired,
                                   probability, probability_low AS "ProbabilityLow", probability_high AS "ProbabilityHigh", expectancy,
                                   expectancy_low AS "ExpectancyLow", expectancy_high AS "ExpectancyHigh", signals,
                                   own_probability AS "OwnProbability", own_probability_low AS "OwnProbabilityLow", own_probability_high AS "OwnProbabilityHigh",
                                   own_expectancy AS "OwnExpectancy", own_expectancy_low AS "OwnExpectancyLow", own_expectancy_high AS "OwnExpectancyHigh",
                                   parent_weight AS "ParentWeight", few_own_cases AS "FewOwnCases", gross_expectancy AS "GrossExpectancy",
                                   gross_expectancy_low AS "GrossExpectancyLow", gross_expectancy_high AS "GrossExpectancyHigh"
                                   """;

    public async Task ReplaceAsync(
        int engineVersion,
        IReadOnlyCollection<IdeaBucket> buckets,
        IReadOnlyCollection<RiskRewardCurve> curves,
        (IdeaType Type, Interval Interval)? only,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """DELETE FROM idea_buckets WHERE @Type::text IS NULL OR engine_version <> @Version OR (type = @Type AND "interval" = @Interval)""",
            new { Version = engineVersion, Type = only?.Type.ToCode(), Interval = only?.Interval.ToCode() },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO idea_buckets (engine_version, days, setup, type, "interval", side, category, cases, targets, stops, expired, probability,
                    probability_low, probability_high, expectancy, expectancy_low, expectancy_high, signals, own_probability, own_probability_low,
                    own_probability_high, own_expectancy, own_expectancy_low, own_expectancy_high, parent_weight, few_own_cases, gross_expectancy,
                    gross_expectancy_low, gross_expectancy_high)
                VALUES (@EngineVersion, @Days, @Setup, @Type, @Interval, @Side, @Category, @Cases, @Targets, @Stops, @Expired, @Probability,
                    @ProbabilityLow, @ProbabilityHigh, @Expectancy, @ExpectancyLow, @ExpectancyHigh, @Signals, @OwnProbability, @OwnProbabilityLow,
                    @OwnProbabilityHigh, @OwnExpectancy, @OwnExpectancyLow, @OwnExpectancyHigh, @ParentWeight, @FewOwnCases, @GrossExpectancy,
                    @GrossExpectancyLow, @GrossExpectancyHigh)
                """,
                buckets.Select(bucket => Row(engineVersion, bucket)),
                transaction,
                cancellationToken: cancellationToken));
        // The recompute of a type × interval replaces the curve of its interval only when the type is «Уровни»; that of another type has none.
        await connection.ExecuteAsync(new CommandDefinition(
            """DELETE FROM idea_risk_reward_curves WHERE @Whole OR engine_version <> @Version OR "interval" = @Interval""",
            new { Whole = only is null, Version = engineVersion, Interval = only is { Type: IdeaType.Levels } levels ? levels.Interval.ToCode() : null },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO idea_risk_reward_curves (engine_version, "interval", side, category, band, cases, targets)
                VALUES (@EngineVersion, @Interval, @Side, @Category, @Band, @Cases, @Targets)
                """,
                curves.SelectMany(curve => curve.Bands.Select(band => new RiskRewardBandDb
                {
                    EngineVersion = engineVersion,
                    Interval = curve.Interval.ToCode(),
                    Side = curve.Side.ToCode(),
                    Category = curve.Category.ToCode(),
                    Band = band.Band.ToCode(),
                    Cases = band.Cases,
                    Targets = band.Targets
                })),
                transaction,
                cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RiskRewardCurve>> GetCurvesAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<RiskRewardBandDb> rows = await connection.QueryAsync<RiskRewardBandDb>(
            new CommandDefinition(
                """
                SELECT engine_version AS "EngineVersion", "interval", side, category, band, cases, targets
                FROM idea_risk_reward_curves WHERE engine_version = @Version
                """,
                new { Version = engineVersion },
                cancellationToken: cancellationToken));

        return
        [
            .. rows.GroupBy(row => (row.Interval, row.Side, row.Category)).Select(set =>
            {
                MarketDataCodes.TryParseInterval(set.Key.Interval, out Interval interval);
                IdeaCodes.TryParseSide(set.Key.Side, out IdeaSide side);
                MarketDataCodes.TryParseCategory(set.Key.Category, out MarketCategory category);

                return new RiskRewardCurve(interval, side, category, [.. set.Select(row => new RiskRewardBandCount(IdeaCodes.ParseBand(row.Band), row.Cases, row.Targets))]);
            })
        ];
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
            Targets = statistics.Targets,
            Stops = statistics.Stops,
            Expired = statistics.Expired,
            Probability = statistics.Probability,
            ProbabilityLow = statistics.ProbabilityLow,
            ProbabilityHigh = statistics.ProbabilityHigh,
            Expectancy = statistics.Expectancy,
            ExpectancyLow = statistics.ExpectancyLow,
            ExpectancyHigh = statistics.ExpectancyHigh,
            Signals = statistics.Signals,
            OwnProbability = statistics.OwnProbability,
            OwnProbabilityLow = statistics.OwnProbabilityLow,
            OwnProbabilityHigh = statistics.OwnProbabilityHigh,
            OwnExpectancy = statistics.OwnExpectancy,
            OwnExpectancyLow = statistics.OwnExpectancyLow,
            OwnExpectancyHigh = statistics.OwnExpectancyHigh,
            ParentWeight = statistics.ParentWeight,
            FewOwnCases = statistics.FewOwnCases,
            GrossExpectancy = statistics.GrossExpectancy,
            GrossExpectancyLow = statistics.GrossExpectancyLow,
            GrossExpectancyHigh = statistics.GrossExpectancyHigh
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
            new BucketStatistics(row.Cases, row.Targets, row.Stops, row.Expired, row.Probability, row.ProbabilityLow, row.ProbabilityHigh, row.Expectancy,
                row.ExpectancyLow, row.ExpectancyHigh, row.Signals)
            {
                OwnProbability = row.OwnProbability,
                OwnProbabilityLow = row.OwnProbabilityLow,
                OwnProbabilityHigh = row.OwnProbabilityHigh,
                OwnExpectancy = row.OwnExpectancy,
                OwnExpectancyLow = row.OwnExpectancyLow,
                OwnExpectancyHigh = row.OwnExpectancyHigh,
                ParentWeight = row.ParentWeight,
                FewOwnCases = row.FewOwnCases,
                GrossExpectancy = row.GrossExpectancy,
                GrossExpectancyLow = row.GrossExpectancyLow,
                GrossExpectancyHigh = row.GrossExpectancyHigh
            });
    }
}