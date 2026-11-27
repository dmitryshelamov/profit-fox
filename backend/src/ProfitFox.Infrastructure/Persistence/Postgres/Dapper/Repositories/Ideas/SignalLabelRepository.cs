using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Scoring;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     The labels of signals in <c>signal_labels</c> (change add-ideas-and-market, design.md, decision 7): the text of a label
///     that is on is one of its kind by a unique index.
/// </summary>
internal sealed class SignalLabelRepository(NpgsqlDataSource database) : ISignalLabelRepository
{
    private const string UniqueViolation = "23505";

    public async Task<SignalLabel?> FindAsync(Guid signalId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(SignalLabelRepositoryQueries.Find, new { SignalId = signalId }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    // The weights of the label go to its new text in the same transaction, and the ideas of both texts get their scores again
    // (change add-idea-score, design.md, decisions 1 and 2); a label turned off or on with the same text keeps them.
    public async Task<bool> SetAsync(SignalLabel label, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            string? before = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                SignalLabelRepositoryQueries.TextOf,
                new { label.SignalId },
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                SignalLabelRepositoryQueries.Set,
                new { label.SignalId, label.Enabled, label.Text, Timeframe = Timeframes.Name(label.Timeframe) },
                transaction,
                cancellationToken: cancellationToken));
            if (before is not null && before != label.Text)
            {
                await LabelWeightRepository.MoveAsync(connection, transaction, before, label.Text, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SignalLabel>> AllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<Row>(new CommandDefinition(SignalLabelRepositoryQueries.All, cancellationToken: cancellationToken))).Select(row => row.ToDomain())];
    }

    private sealed class Row
    {
        public Guid SignalId { get; init; }

        public bool Enabled { get; init; }

        public string Text { get; init; } = "";

        public string Timeframe { get; init; } = "";

        public SignalLabel ToDomain()
        {
            return new SignalLabel(SignalId, Enabled, Text, Timeframes.TryParse(Timeframe, out TimeSpan length) ? length : throw new InvalidOperationException($"{Timeframe} is not a timeframe."));
        }
    }
}
