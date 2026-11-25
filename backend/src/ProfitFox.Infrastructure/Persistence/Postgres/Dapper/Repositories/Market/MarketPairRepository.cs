using Dapper;

using Npgsql;

using NpgsqlTypes;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Lists;
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
        command.Parameters.Add(Array("Kind", NpgsqlDbType.Text, pairs.Select(pair => (object?)InstrumentKinds.Name(pair.Kind))));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ListPage<MarketPairRow>> ListAsync(MarketListQuery query, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        string where = Where(query, parameters);
        parameters.Add("Limit", query.Order.PageSize);
        parameters.Add("Offset", query.Order.Offset);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        int total = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*) FROM market_pairs m WHERE {where}", parameters, cancellationToken: cancellationToken));
        string sql = $"SELECT {MarketListQueries.Columns} FROM market_pairs m WHERE {where} {ListColumns.OrderBy(ListColumns.Market, query.Order, "m.symbol")} LIMIT @Limit OFFSET @Offset";
        Row[] rows = [.. await connection.QueryAsync<Row>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))];
        return new ListPage<MarketPairRow>(await WithIdeas(connection, rows, cancellationToken), total, query.Order.Page, query.Order.PageSize);
    }

    public async Task<MarketPairRow?> FindAsync(string symbol, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row[] rows = [.. await connection.QueryAsync<Row>(new CommandDefinition($"SELECT {MarketListQueries.Columns} FROM market_pairs m WHERE m.symbol = @Symbol", new { Symbol = symbol }, cancellationToken: cancellationToken))];
        return rows.Length == 0 ? null : (await WithIdeas(connection, rows, cancellationToken))[0];
    }

    public async Task<FilterCount> CountAsync(IReadOnlyList<FilterCondition> conditions, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Search", null, System.Data.DbType.String);
        parameters.Add("Strategy", null, System.Data.DbType.String);
        parameters.Add("Timeframe", null, System.Data.DbType.String);
        string market = ListColumns.Conditions(conditions, FieldPlace.Market, parameters, "c");
        string idea = ListColumns.Conditions(conditions, FieldPlace.Idea, parameters, "c");
        string pairs = ListColumns.Has(conditions, FieldPlace.Idea) ? $"{market} AND {MarketListQueries.OpenIdea(idea)}" : market;
        string sql =
            $"""
             SELECT
                 (SELECT count(*) FROM market_pairs m WHERE {pairs})::int AS "Pairs",
                 (SELECT count(*) FROM market_pairs)::int AS "AllPairs",
                 (SELECT count(*) FROM ideas i LEFT JOIN strategies s ON s.id = i.strategy_id LEFT JOIN market_pairs m ON m.symbol = i.symbol
                  WHERE i.state = 'open' AND {idea} AND {market})::int AS "Ideas",
                 (SELECT count(*) FROM ideas WHERE state = 'open')::int AS "AllIdeas"
             """;
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<FilterCount>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    // The search, the kind, the choices of an idea and the conditions of a filter, each value a parameter.
    private static string Where(MarketListQuery query, DynamicParameters parameters)
    {
        string kind = "TRUE";
        if (query.Kind is { } chosen)
        {
            parameters.Add("Kind", InstrumentKinds.Name(chosen));
            kind = "m.kind = @Kind";
        }

        parameters.Add("Search", string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), System.Data.DbType.String);
        parameters.Add("Strategy", string.IsNullOrEmpty(query.Strategy) ? null : query.Strategy, System.Data.DbType.String);
        parameters.Add("Timeframe", string.IsNullOrEmpty(query.Timeframe) ? null : query.Timeframe, System.Data.DbType.String);
        string market = ListColumns.Conditions(query.Conditions, FieldPlace.Market, parameters, "c");
        string idea = ListColumns.Conditions(query.Conditions, FieldPlace.Idea, parameters, "c");
        bool ofIdea = query.IdeasOnly || !string.IsNullOrEmpty(query.Strategy) || !string.IsNullOrEmpty(query.Timeframe) || ListColumns.Has(query.Conditions, FieldPlace.Idea);
        return ofIdea ? $"{MarketListQueries.Search} AND {kind} AND {market} AND {MarketListQueries.OpenIdea(idea)}" : $"{MarketListQueries.Search} AND {kind} AND {market}";
    }

    private static async Task<IReadOnlyList<MarketPairRow>> WithIdeas(NpgsqlConnection connection, Row[] rows, CancellationToken cancellationToken)
    {
        string[] symbols = [.. rows.Select(row => row.Symbol)];
        ILookup<string, Badge> badges = (await connection.QueryAsync<Badge>(new CommandDefinition(MarketListQueries.Badges, new { Symbols = symbols }, cancellationToken: cancellationToken)))
            .ToLookup(badge => badge.Symbol, StringComparer.Ordinal);
        return [.. rows.Select(row => new MarketPairRow(row.ToDomain(), [.. badges[row.Symbol].Select(badge => new IdeaBadge(badge.Strategy, badge.Timeframe, badge.Side))]))];
    }

    private static NpgsqlParameter Array(string name, NpgsqlDbType type, IEnumerable<object?> values)
    {
        return new NpgsqlParameter(name, NpgsqlDbType.Array | type) { Value = values.Select(value => value ?? DBNull.Value).ToArray() };
    }

    private sealed class Row
    {
        public string Symbol { get; init; } = "";

        public decimal Price { get; init; }

        public decimal? Change1h { get; init; }

        public decimal? Change4h { get; init; }

        public decimal? Change24h { get; init; }

        public decimal? Turnover24h { get; init; }

        public decimal? OpenInterest { get; init; }

        public decimal? OpenInterestUsdt { get; init; }

        public decimal? FundingRate { get; init; }

        public DateTimeOffset? NextFunding { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public string Kind { get; init; } = "crypto";

        public MarketPair ToDomain()
        {
            InstrumentKind kind = InstrumentKinds.TryParse(Kind, out InstrumentKind known) ? known : InstrumentKind.Crypto;
            return new MarketPair(Symbol, Price, Change1h, Change4h, Change24h, Turnover24h, OpenInterest, OpenInterestUsdt, FundingRate, NextFunding, UpdatedAt, kind);
        }
    }

    private sealed record Badge(string Symbol, string Strategy, string Timeframe, string Side);
}
