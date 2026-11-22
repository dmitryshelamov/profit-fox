using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Lists;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     The ideas in <c>ideas</c> (change add-ideas-and-market, design.md, decisions 5 and 14). A second idea of a strategy on a
///     pair, timeframe and candle is refused by a unique index, so an idea is written once even by two scanners.
/// </summary>
internal sealed class IdeaRepository(NpgsqlDataSource database) : IIdeaRepository
{
    public async Task<bool> AddAsync(Idea idea, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(IdeaRepositoryQueries.Insert, IdeaDbConverter.Parameters(idea), cancellationToken: cancellationToken)) == 1;
    }

    public async Task SaveAsync(Idea idea, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(IdeaRepositoryQueries.Save, IdeaDbConverter.Parameters(idea), cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Idea>> OpenAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<IdeaDb>(new CommandDefinition(IdeaRepositoryQueries.Open, cancellationToken: cancellationToken))).Select(IdeaDbConverter.ToDomain)];
    }

    public async Task<Idea?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IdeaDb? row = await connection.QuerySingleOrDefaultAsync<IdeaDb>(new CommandDefinition(IdeaRepositoryQueries.Find, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : IdeaDbConverter.ToDomain(row);
    }

    public async Task<ListPage<Idea>> ListAsync(IdeaListQuery query, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        string where = Where(query, parameters);
        parameters.Add("Limit", query.Order.PageSize);
        parameters.Add("Offset", query.Order.Offset);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        int total = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*) {Joined} WHERE {where}", parameters, cancellationToken: cancellationToken));
        string sql = $"SELECT {IdeaRepositoryQueries.Columns} {Joined} WHERE {where} {ListColumns.OrderBy(ListColumns.Ideas, query.Order, "i.moment DESC, i.id")} LIMIT @Limit OFFSET @Offset";
        IdeaDb[] rows = [.. await connection.QueryAsync<IdeaDb>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))];
        return new ListPage<Idea>([.. rows.Select(IdeaDbConverter.ToDomain)], total, query.Order.Page, query.Order.PageSize);
    }

    public async Task<ListPage<IdeaSummary>> SummaryAsync(IdeaListQuery query, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        string where = Where(query with { Kind = IdeaListKind.Closed }, parameters);
        parameters.Add("Limit", query.Order.PageSize);
        parameters.Add("Offset", query.Order.Offset);
        string groups =
            $"""
             SELECT i.strategy_id AS "StrategyId", max({ListColumns.StrategyName}) AS "Strategy", i.strategy_version AS "Version", i.timeframe AS "Timeframe",
                    count(*)::int AS "Ideas", avg(CASE WHEN i.result > 0 THEN 1.0 ELSE 0.0 END)::float8 AS "Wins", avg(i.result)::float8 AS "Mean",
                    COALESCE(sum(i.result_usdt), 0)::float8 AS "Sum"
             {Joined} WHERE {where}
             GROUP BY i.strategy_id, i.strategy_version, i.timeframe
             """;
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        int total = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*) FROM ({groups}) g", parameters, cancellationToken: cancellationToken));
        string sql = $"SELECT * FROM ({groups}) g {ListColumns.OrderBy(ListColumns.Summary, query.Order, "\"Strategy\", \"Version\"")} LIMIT @Limit OFFSET @Offset";
        IdeaSummary[] rows = [.. await connection.QueryAsync<IdeaSummary>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))];
        return new ListPage<IdeaSummary>(rows, total, query.Order.Page, query.Order.PageSize);
    }

    public async Task<(int Open, int Closed)> CountsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<(int, int)>(new CommandDefinition(
            "SELECT (count(*) FILTER (WHERE state = 'open'))::int, (count(*) FILTER (WHERE state = 'closed'))::int FROM ideas",
            cancellationToken: cancellationToken));
    }

    private const string Joined = "FROM ideas i LEFT JOIN strategies s ON s.id = i.strategy_id LEFT JOIN market_pairs m ON m.symbol = i.symbol";

    // The kind of the list, the choices and the conditions of a filter, each value a parameter.
    private static string Where(IdeaListQuery query, DynamicParameters parameters)
    {
        List<string> parts =
        [
            query.Kind switch
            {
                IdeaListKind.Open => "i.state = 'open'",
                IdeaListKind.Closed => "i.state = 'closed'",
                _ => "i.symbol = @Symbol"
            }
        ];
        parameters.Add("Symbol", query.Symbol, System.Data.DbType.String);
        Add(parts, parameters, "Search", query.Search?.Trim(), "strpos(upper(i.symbol), upper(@Search)) > 0");
        Add(parts, parameters, "Strategy", query.Strategy, $"{ListColumns.StrategyName} = @Strategy");
        Add(parts, parameters, "Timeframe", query.Timeframe, "i.timeframe = @Timeframe");
        Add(parts, parameters, "Side", query.Side, "i.side = @Side");
        Add(parts, parameters, "Label", query.Label, "@Label = ANY(i.labels)");
        Add(parts, parameters, "ExitReason", query.ExitReason, "i.exit_reason = @ExitReason");
        Add(parts, parameters, "State", query.State, "i.state = @State");
        if (query.OnlyPositive)
        {
            parts.Add("i.now_result > 0");
        }

        if (query.OnlyWithStop)
        {
            parts.Add("i.stop IS NOT NULL");
        }

        if (query.PeriodDays is { } days)
        {
            parameters.Add("Days", days);
            parts.Add("i.exit_time >= now() - make_interval(days => @Days)");
        }

        parts.Add(ListColumns.Conditions(query.Conditions, FieldPlace.Idea, parameters, "c"));
        parts.Add(ListColumns.Conditions(query.Conditions, FieldPlace.Market, parameters, "c"));
        return string.Join(" AND ", parts);
    }

    private static void Add(List<string> parts, DynamicParameters parameters, string name, string? value, string condition)
    {
        if (!string.IsNullOrEmpty(value))
        {
            parameters.Add(name, value);
            parts.Add(condition);
        }
    }
}
