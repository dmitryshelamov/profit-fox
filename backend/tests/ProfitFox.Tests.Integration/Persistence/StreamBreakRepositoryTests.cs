using System.Globalization;

using Dapper;

using Npgsql;

using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class StreamBreakRepositoryTests
{
    // Scenario "Соединение восстановилось".
    [Test]
    public async Task Break_is_stored_with_its_stream_and_both_times()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        var streamBreak = new StreamBreak(
            "liquidations",
            DateTimeOffset.Parse("2026-11-08T03:10:05Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-11-08T03:10:41Z", CultureInfo.InvariantCulture));

        await new StreamBreakRepository(dataSource).AddAsync(streamBreak, CancellationToken.None);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        IEnumerable<(string, DateTime, DateTime)> rows = await connection.QueryAsync<(string, DateTime, DateTime)>("SELECT stream, lost_at, restored_at FROM stream_breaks");
        rows.Select(row => new StreamBreak(row.Item1, new DateTimeOffset(row.Item2, TimeSpan.Zero), new DateTimeOffset(row.Item3, TimeSpan.Zero))).ShouldBe([streamBreak]);
    }
}