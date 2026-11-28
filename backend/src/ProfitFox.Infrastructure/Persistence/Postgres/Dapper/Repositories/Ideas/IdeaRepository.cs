using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Lists;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Scoring.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     The ideas in <c>ideas</c> (change add-ideas-and-market, design.md, decisions 5 and 14). A second idea of a strategy on a
///     pair, timeframe and candle is refused by a unique index, so an idea is written once even by two scanners.
/// </summary>
internal sealed class IdeaRepository(NpgsqlDataSource database) : IIdeaRepository
{
    public async Task<bool> AddAsync(Idea idea, CancellationToken cancellationToken)
    {
        // The score is written from the weights under the shared lock of the weights (design.md, decision 2): a change of a
        // weight that began first ends before the insert reads them, and one that begins later counts this idea again. The lock
        // is a statement of its own, so the insert reads the weights after it has it.
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(IdeaScoreQueries.ShareWeights, transaction: transaction, cancellationToken: cancellationToken));
        bool added = await connection.ExecuteAsync(new CommandDefinition(IdeaRepositoryQueries.Insert, IdeaDbConverter.Parameters(idea), transaction, cancellationToken: cancellationToken)) == 1;
        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    public async Task SaveAsync(Idea idea, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(IdeaRepositoryQueries.Save, IdeaDbConverter.Parameters(idea), cancellationToken: cancellationToken));
    }

    public async Task SaveAllAsync(IReadOnlyCollection<Idea> ideas, CancellationToken cancellationToken)
    {
        if (ideas.Count == 0)
        {
            return;
        }

        // Under the shared lock of the weights, as an insert: a change of a weight counts the scores of many of these rows
        // again, and the two statements interleaved would take the locks of the rows in different orders — a deadlock that
        // stops the scanner. Saves do not wait for each other; one begun during a change waits for its end.
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(IdeaScoreQueries.ShareWeights, transaction: transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(IdeaRepositoryQueries.SaveAll, IdeaDbConverter.Batch(ideas), transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
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
        string orderBy = ListColumns.OrderBy(ListColumns.Ideas, query.Order, "i.moment DESC, i.id");
        ListOrder order = query.Order;
        if (query.Around is { } around)
        {
            // The page the idea is on in this order (task 18.22); the page asked when it is not in the list.
            parameters.Add("Around", around);
            long? position = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT r.position FROM (SELECT i.id, row_number() OVER ({orderBy}) AS position {Joined} WHERE {where}) r WHERE r.id = @Around",
                parameters,
                cancellationToken: cancellationToken));
            if (position is { } found)
            {
                order = order with { Page = (int)((found - 1) / order.PageSize) + 1 };
                parameters.Add("Offset", order.Offset);
            }
        }

        string sql = $"SELECT {IdeaRepositoryQueries.Columns} {Joined} WHERE {where} {orderBy} LIMIT @Limit OFFSET @Offset";
        IdeaDb[] rows = [.. await connection.QueryAsync<IdeaDb>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))];
        return new ListPage<Idea>([.. rows.Select(IdeaDbConverter.ToDomain)], total, order.Page, order.PageSize);
    }

    // A row of the summary is one of a strategy, its version and a timeframe: the order ends with all three, so pages with
    // equal values of the sort neither repeat nor lose a row (task 18.16).
    private const string SummaryTieBreak = "\"Strategy\", \"Version\", \"Timeframe\", \"StrategyId\"";

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
        string sql = $"SELECT * FROM ({groups}) g {ListColumns.OrderBy(ListColumns.Summary, query.Order, SummaryTieBreak)} LIMIT @Limit OFFSET @Offset";
        IdeaSummary[] rows = [.. await connection.QueryAsync<IdeaSummary>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))];
        return new ListPage<IdeaSummary>(rows, total, query.Order.Page, query.Order.PageSize);
    }

    public async Task<IReadOnlyList<IdeaStrategyCount>> StrategyCountsAsync(IdeaListQuery query, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        string where = Where(query with { Strategy = null }, parameters);
        string sql = $"SELECT {ListColumns.StrategyName} AS \"Strategy\", count(*)::int AS \"Ideas\" {Joined} WHERE {where} GROUP BY 1";
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<IdeaStrategyCount>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))];
    }

    // A group of half a point a row (change decimal-label-weights, design.md, decision 4): the group of a score is the
    // greatest multiple of 0.5 not above it, so a whole score begins its group and −0.25 is in the one from −0.5.
    public async Task<IReadOnlyList<IdeaScoreRow>> ScoresAsync(IdeaListQuery query, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        string where = Where(query with { Kind = IdeaListKind.Closed }, parameters);
        string sql =
            $"""
             SELECT g.band AS "From", g.band + 0.5 AS "To", count(*)::int AS "Ideas",
                    avg(CASE WHEN g.result > 0 THEN 1.0 ELSE 0.0 END)::float8 AS "Wins",
                    avg(g.result)::float8 AS "Mean", COALESCE(sum(g.result_usdt), 0)::float8 AS "Sum"
             FROM (SELECT (floor(i.score * 2) / 2)::numeric(7,1) AS band, i.result, i.result_usdt {Joined} WHERE {where}) g
             GROUP BY g.band
             ORDER BY g.band DESC
             """;
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<IdeaScoreRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))];
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
        // The kind of the pair in the snapshot: an idea of a pair out of it has no row there and is of no kind (decision 3).
        Add(parts, parameters, "PairKind", query.PairKind is { } kind ? InstrumentKinds.Name(kind) : null, "m.kind = @PairKind");
        if (query.MinScore is { } minScore)
        {
            parameters.Add("MinScore", minScore);
            parts.Add("i.score >= @MinScore");
        }

        if (query.OnlyPositive)
        {
            parts.Add("i.now_result > 0");
        }

        if (query.OnlyWithStop)
        {
            parts.Add("i.stop IS NOT NULL");
        }

        // The ideas that touch the window of the chart: born by its end and not gone before its start (task 18.22).
        if (query.To is { } to)
        {
            parameters.Add("To", to);
            parts.Add("i.moment <= @To");
        }

        if (query.From is { } from)
        {
            parameters.Add("From", from);
            parts.Add("COALESCE(i.exit_time, i.until) >= @From");
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
