using Dapper;

using Npgsql;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>The exit rules applied, one row per type × interval of <c>idea_exit_rules</c> (decision 5).</summary>
internal sealed class IdeaExitRuleRepository(NpgsqlDataSource dataSource) : IIdeaExitRuleRepository
{
    public async Task<IReadOnlyList<IdeaExitRule>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<Row> rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT type, "interval", life_candles AS "LifeCandles", target_r AS "TargetR", entry_candles AS "EntryCandles", applied_at AS "AppliedAt"
            FROM idea_exit_rules
            """,
            cancellationToken: cancellationToken));

        return [.. rows.Select(row =>
        {
            IdeaCodes.TryParseType(row.Type, out IdeaType type);
            MarketDataCodes.TryParseInterval(row.Interval, out Interval interval);

            return new IdeaExitRule(type, interval, row.LifeCandles, row.TargetR, row.EntryCandles, row.AppliedAt);
        })];
    }

    public async Task SaveAsync(IdeaExitRule rule, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO idea_exit_rules (type, "interval", life_candles, target_r, entry_candles, applied_at)
            VALUES (@Type, @Interval, @LifeCandles, @TargetR, @EntryCandles, @AppliedAt)
            ON CONFLICT (type, "interval") DO UPDATE SET life_candles = EXCLUDED.life_candles, target_r = EXCLUDED.target_r,
                entry_candles = EXCLUDED.entry_candles, applied_at = EXCLUDED.applied_at
            """,
            new { Type = rule.Type.ToCode(), Interval = rule.Interval.ToCode(), rule.LifeCandles, rule.TargetR, rule.EntryCandles, rule.AppliedAt },
            cancellationToken: cancellationToken));
    }

    private sealed class Row
    {
        public required string Type { get; init; }

        public required string Interval { get; init; }

        public int LifeCandles { get; init; }

        public decimal? TargetR { get; init; }

        public int? EntryCandles { get; init; }

        public DateTimeOffset AppliedAt { get; init; }
    }
}