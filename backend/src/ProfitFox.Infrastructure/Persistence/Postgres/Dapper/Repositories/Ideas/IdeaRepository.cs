using Dapper;

using Npgsql;

using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

internal sealed class IdeaRepository(NpgsqlDataSource dataSource) : IIdeaRepository
{
    public Task<IReadOnlyList<Idea>> SaveAsync(IReadOnlyCollection<Idea> created, IReadOnlyCollection<Idea> changed, CancellationToken cancellationToken)
    {
        return SaveCoreAsync(created, changed, [], null, cancellationToken);
    }

    public Task<IReadOnlyList<Idea>> SaveAsync(
        IReadOnlyCollection<Idea> created,
        IReadOnlyCollection<Idea> changed,
        IReadOnlyCollection<ClosedIdea> outcomes,
        CancellationToken cancellationToken)
    {
        return SaveCoreAsync(created, changed, outcomes, null, cancellationToken);
    }

    public Task<IReadOnlyList<Idea>> SaveAsync(
        IReadOnlyCollection<Idea> created,
        IReadOnlyCollection<Idea> changed,
        IReadOnlyCollection<ClosedIdea> outcomes,
        AutoMinute autos,
        CancellationToken cancellationToken)
    {
        return SaveCoreAsync(created, changed, outcomes, autos, cancellationToken);
    }

    private async Task<IReadOnlyList<Idea>> SaveCoreAsync(
        IReadOnlyCollection<Idea> created,
        IReadOnlyCollection<Idea> changed,
        IReadOnlyCollection<ClosedIdea> outcomes,
        AutoMinute? autos,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The autos write under the lease of the stream, first of all: a scanner that lost it writes no second paper trade.
        if (autos is not null && await connection.ExecuteScalarAsync<int?>(MarketSnapshotRepositoryQueries.LockLease(autos.Owner, transaction, cancellationToken)) is null)
        {
            throw new ScannerLeaseLostException();
        }

        // The moved ideas go first: one that closed gives its key back to an idea made on the same minute.
        await connection.ExecuteAsync(IdeaRepositoryQueries.Update(changed.Select(idea => IdeaDbConverter.FromDomain(idea)), transaction, cancellationToken));
        await connection.ExecuteAsync(IdeaRepositoryQueries.WriteTradeOutcomes(outcomes, transaction, cancellationToken));
        await connection.ExecuteAsync(PaperTradeQueries.WriteOutcomes(
            outcomes.Select(closed => (object)new { closed.IdeaId, Status = closed.Outcome.Status.ToCode(), closed.Outcome.StatusAt, closed.Outcome.Result, closed.Outcome.ExitPrice }),
            transaction,
            cancellationToken));
        var added = new List<Idea>();

        foreach (Idea idea in created)
        {
            if (await connection.ExecuteScalarAsync<Guid?>(IdeaRepositoryQueries.Insert(IdeaDbConverter.FromDomain(idea), transaction, cancellationToken)) is not null)
            {
                added.Add(idea);
            }
        }

        if (autos is not null)
        {
            // What the autos did with an idea that storage refused is left out.
            var taken = added.Select(idea => idea.Id).ToHashSet();
            await connection.ExecuteAsync(PaperTradeQueries.Insert(autos.Opened.Where(paper => taken.Contains(paper.Trade.IdeaId)).Select(PaperTradeDbConverter.FromDomain), transaction, cancellationToken));
            await connection.ExecuteAsync(PaperTradeQueries.Update(autos.Stepped.Select(PaperTradeDbConverter.FromDomain), transaction, cancellationToken));
            await connection.ExecuteAsync(
                IdeaAutoEntryRepositoryQueries.Insert(autos.Entries.Where(entry => taken.Contains(entry.IdeaId)).Select(IdeaAutoDbConverter.FromDomain), transaction, cancellationToken));
            await connection.ExecuteAsync(
                IdeaAutoRefusalRepositoryQueries.InsertMany(autos.Refusals.Where(refusal => taken.Contains(refusal.IdeaId)).Select(IdeaAutoDbConverter.FromDomain), transaction, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        return added;
    }

    public async Task<IReadOnlyList<Idea>> GetOpenAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaDb>(IdeaRepositoryQueries.GetOpen(cancellationToken))).Select(IdeaDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<Idea>> GetClosedOfTradesWithoutOutcomeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaDb>(IdeaRepositoryQueries.GetClosedOfTradesWithoutOutcome(cancellationToken))).Select(IdeaDbConverter.ToDomain)];
    }

    public async Task<IReadOnlyList<Idea>> GetRecentAsync(DateTimeOffset closedSince, Instrument? instrument, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<IdeaDb> rows = await connection.QueryAsync<IdeaDb>(
            IdeaRepositoryQueries.GetRecent(closedSince, instrument?.Category.ToCode(), instrument?.Symbol, cancellationToken));

        return [.. rows.Select(IdeaDbConverter.ToDomain)];
    }

    public async Task<Idea?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IdeaDb? row = await connection.QuerySingleOrDefaultAsync<IdeaDb>(IdeaRepositoryQueries.Get(id, cancellationToken));

        return row is null ? null : IdeaDbConverter.ToDomain(row);
    }

    public async Task<IReadOnlyList<Idea>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<IdeaDb>(IdeaRepositoryQueries.GetMany(ids, cancellationToken))).Select(IdeaDbConverter.ToDomain)];
    }

    public async Task<int> DeleteClosedBeforeAsync(DateTimeOffset closedBefore, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(IdeaRepositoryQueries.DeleteClosedBefore(closedBefore, cancellationToken));
    }
}