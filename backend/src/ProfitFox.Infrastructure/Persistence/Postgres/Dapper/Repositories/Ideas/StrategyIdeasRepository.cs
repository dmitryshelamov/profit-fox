using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     Whether strategies give ideas, in <c>strategy_ideas</c> (change add-ideas-and-market, design.md, decision 6); the
///     timeframes are kept by their names.
/// </summary>
internal sealed class StrategyIdeasRepository(NpgsqlDataSource database) : IStrategyIdeasRepository
{
    public async Task<StrategyIdeas?> FindAsync(Guid strategyId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(StrategyIdeasRepositoryQueries.Find, new { StrategyId = strategyId }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task SetAsync(StrategyIdeas ideas, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            StrategyIdeasRepositoryQueries.Set,
            new { ideas.StrategyId, ideas.Enabled, Timeframes = ideas.Timeframes.Select(Timeframes.Name).ToArray() },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<StrategyIdeas>> AllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<Row>(new CommandDefinition(StrategyIdeasRepositoryQueries.All, cancellationToken: cancellationToken))).Select(row => row.ToDomain())];
    }

    private sealed class Row
    {
        public Guid StrategyId { get; init; }

        public bool Enabled { get; init; }

        public string[] Timeframes { get; init; } = [];

        public StrategyIdeas ToDomain()
        {
            return new StrategyIdeas(
                StrategyId,
                Enabled,
                [.. Timeframes.Select(name => Domain.ValueObjects.MarketData.Timeframes.TryParse(name, out TimeSpan length) ? length : throw new InvalidOperationException($"{name} is not a timeframe."))]);
        }
    }
}
