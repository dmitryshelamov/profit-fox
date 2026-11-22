using Npgsql;

using NpgsqlTypes;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

/// <summary>
///     The snapshot of the market in <c>market_pairs</c> (change add-ideas-and-market, design.md, decision 8): the scanner
///     writes it whole, a column of values per field, in one statement.
/// </summary>
internal sealed class MarketPairRepository(NpgsqlDataSource database) : IMarketPairRepository
{
    public async Task ReplaceAsync(IReadOnlyList<MarketPair> pairs, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(MarketPairRepositoryQueries.Replace, connection);
        command.Parameters.Add(Array("Symbol", NpgsqlDbType.Text, pairs.Select(pair => (object?)pair.Symbol)));
        command.Parameters.Add(Array("Price", NpgsqlDbType.Numeric, pairs.Select(pair => (object?)pair.Price)));
        command.Parameters.Add(Array("Change1h", NpgsqlDbType.Numeric, pairs.Select(pair => (object?)pair.Change1h)));
        command.Parameters.Add(Array("Change4h", NpgsqlDbType.Numeric, pairs.Select(pair => (object?)pair.Change4h)));
        command.Parameters.Add(Array("Change24h", NpgsqlDbType.Numeric, pairs.Select(pair => (object?)pair.Change24h)));
        command.Parameters.Add(Array("Turnover24h", NpgsqlDbType.Numeric, pairs.Select(pair => (object?)pair.Turnover24h)));
        command.Parameters.Add(Array("OpenInterest", NpgsqlDbType.Numeric, pairs.Select(pair => (object?)pair.OpenInterest)));
        command.Parameters.Add(Array("OpenInterestUsdt", NpgsqlDbType.Numeric, pairs.Select(pair => (object?)pair.OpenInterestUsdt)));
        command.Parameters.Add(Array("FundingRate", NpgsqlDbType.Numeric, pairs.Select(pair => (object?)pair.FundingRate)));
        command.Parameters.Add(Array("NextFunding", NpgsqlDbType.TimestampTz, pairs.Select(pair => (object?)pair.NextFunding)));
        command.Parameters.Add(Array("UpdatedAt", NpgsqlDbType.TimestampTz, pairs.Select(pair => (object?)pair.UpdatedAt)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlParameter Array(string name, NpgsqlDbType type, IEnumerable<object?> values)
    {
        return new NpgsqlParameter(name, NpgsqlDbType.Array | type) { Value = values.Select(value => value ?? DBNull.Value).ToArray() };
    }
}
