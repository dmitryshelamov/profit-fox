using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Scoring;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Scoring.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Scoring;

/// <summary>
///     The weights of the labels in <c>label_weights</c> (change add-idea-score, design.md, decision 1). The database refuses
///     a weight outside −3…3.
/// </summary>
internal sealed class LabelWeightRepository(NpgsqlDataSource database) : ILabelWeightRepository
{
    public async Task<IReadOnlyList<LabelWeight>> AllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<Row>(new CommandDefinition(LabelWeightRepositoryQueries.All, cancellationToken: cancellationToken))).Select(row => new LabelWeight(row.Text, row.Long, row.Short))];
    }

    public async Task SetAsync(LabelWeight weight, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            LabelWeightRepositoryQueries.Set,
            new { weight.Text, Long = (short)weight.Long, Short = (short)weight.Short },
            cancellationToken: cancellationToken));
    }

    public async Task MoveAsync(string from, string to, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(LabelWeightRepositoryQueries.Move, new { From = from, To = to }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScoringLabel>> LabelsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<LabelRow>(new CommandDefinition(LabelWeightRepositoryQueries.Labels, cancellationToken: cancellationToken)))
            .Select(row => new ScoringLabel(row.Text, row.Enabled, row.Timeframe, row.Signal, row.LongWeight, row.ShortWeight))];
    }

    private sealed class Row
    {
        public string Text { get; init; } = "";

        public short Long { get; init; }

        public short Short { get; init; }
    }

    private sealed class LabelRow
    {
        public string Text { get; init; } = "";

        public bool Enabled { get; init; }

        public string Timeframe { get; init; } = "";

        public string Signal { get; init; } = "";

        public int LongWeight { get; init; }

        public int ShortWeight { get; init; }
    }
}
