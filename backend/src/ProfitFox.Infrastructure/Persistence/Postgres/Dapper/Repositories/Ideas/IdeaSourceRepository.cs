using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     The enabled strategies and labels with their latest version numbers, from <c>strategy_ideas</c> with
///     <c>strategies</c> and <c>signal_labels</c> with <c>signals</c>, in one round trip (change add-ideas-and-market, task
///     18.13).
/// </summary>
internal sealed class IdeaSourceRepository(NpgsqlDataSource database) : IIdeaSourceRepository
{
    public async Task<IdeaSources> SourcesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(IdeaSourceRepositoryQueries.Sources, cancellationToken: cancellationToken));
        StrategyRow[] strategies = [.. await grid.ReadAsync<StrategyRow>()];
        LabelRow[] labels = [.. await grid.ReadAsync<LabelRow>()];
        return new IdeaSources(
            [.. strategies.Select(row => new StrategySource(row.StrategyId, row.Name, row.Version, [.. row.Timeframes.Select(Length)]))],
            [.. labels.Select(row => new LabelSource(row.SignalId, row.Name, row.Version, row.Text, Length(row.Timeframe)))]);
    }

    private static TimeSpan Length(string name)
    {
        return Timeframes.TryParse(name, out TimeSpan length) ? length : throw new InvalidOperationException($"{name} is not a timeframe.");
    }

    private sealed class StrategyRow
    {
        public Guid StrategyId { get; init; }

        public string Name { get; init; } = "";

        public int Version { get; init; }

        public string[] Timeframes { get; init; } = [];
    }

    private sealed class LabelRow
    {
        public Guid SignalId { get; init; }

        public string Name { get; init; } = "";

        public int Version { get; init; }

        public string Text { get; init; } = "";

        public string Timeframe { get; init; } = "";
    }
}
