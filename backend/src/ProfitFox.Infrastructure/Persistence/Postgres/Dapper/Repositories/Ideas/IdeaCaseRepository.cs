using Dapper;

using Npgsql;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

internal sealed class IdeaCaseRepository(NpgsqlDataSource dataSource) : IIdeaCaseRepository
{
    public async Task AddAsync(IReadOnlyCollection<IdeaCase> cases, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = IdeaCaseRepositoryQueries.Insert(dataSource, [.. cases.Select(item => IdeaDbConverter.FromDomain(item.Idea, item.Result))]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IdeaCase>> GetAsync(Instrument instrument, int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<IdeaDb> rows = await connection.QueryAsync<IdeaDb>(
            IdeaCaseRepositoryQueries.Get(instrument.Category.ToCode(), instrument.Symbol, engineVersion, cancellationToken));

        return [.. rows.Select(row => new IdeaCase(IdeaDbConverter.ToDomain(row), row.Result))];
    }

    public async Task<IReadOnlyList<IdeaBucketCounts>> CountAsync(int engineVersion, DateTimeOffset from, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = IdeaCaseRepositoryQueries.Count(connection, engineVersion, from);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var counts = new List<IdeaBucketCounts>();

        while (await reader.ReadAsync(cancellationToken))
        {
            MarketDataCodes.TryParseInterval(reader.GetString(2), out Interval interval);
            MarketDataCodes.TryParseCategory(reader.GetString(4), out MarketCategory category);
            IdeaCodes.TryParseSetup(reader.GetString(0), out IdeaSetup setup);
            IdeaCodes.TryParseType(reader.GetString(1), out IdeaType type);
            IdeaCodes.TryParseSide(reader.GetString(3), out IdeaSide side);
            counts.Add(new IdeaBucketCounts(
                new IdeaBucketKey(setup, type, interval, side, category),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9)));
        }

        return counts;
    }

    public async Task DeleteOtherVersionsAsync(int engineVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(IdeaCaseRepositoryQueries.DeleteOtherVersions(engineVersion, cancellationToken));
    }
}