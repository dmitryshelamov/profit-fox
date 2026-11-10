using Dapper;

using Npgsql;

using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Converters;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

internal sealed class TradeFlowDayRepository(NpgsqlDataSource dataSource) : ITradeFlowDayRepository
{
    private const string SaveSql = """
                                   INSERT INTO trade_flow_days (category, symbol, day, status, "at") VALUES (@Category, @Symbol, @Day, @Status, @At)
                                   ON CONFLICT (category, symbol, day) DO UPDATE SET status = EXCLUDED.status, "at" = EXCLUDED."at"
                                   """;

    private const string GetSql = """
                                  SELECT category AS "Category", symbol AS "Symbol", day AS "Day", status AS "Status", "at" AS "At"
                                  FROM trade_flow_days
                                  WHERE day >= @From AND day < @To
                                  """;

    public async Task SaveAsync(TradeFlowDay day, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                SaveSql,
                new { Category = day.Instrument.Category.ToCode(), day.Instrument.Symbol, day.Day, Status = day.Status.ToCode(), day.At },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TradeFlowDay>> GetAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return await ReadAsync(new CommandDefinition(GetSql, new { From = from, To = to }, cancellationToken: cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<TradeFlowDay>> GetAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return await ReadAsync(
            new CommandDefinition(
                GetSql + " AND category = @Category AND symbol = @Symbol",
                new { From = from, To = to, Category = instrument.Category.ToCode(), instrument.Symbol },
                cancellationToken: cancellationToken),
            cancellationToken);
    }

    private async Task<IReadOnlyList<TradeFlowDay>> ReadAsync(CommandDefinition command, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<TradeFlowDayDb> rows = await connection.QueryAsync<TradeFlowDayDb>(command);

        return [.. rows.Select(TradeFlowDayDbConverter.ToDomain)];
    }
}