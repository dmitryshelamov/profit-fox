using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

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

    public async Task<bool> SetAsync(SignalLabel label, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                SignalLabelRepositoryQueries.Set,
                new { label.SignalId, label.Enabled, label.Text, Timeframe = Timeframes.Name(label.Timeframe) },
                cancellationToken: cancellationToken));
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
