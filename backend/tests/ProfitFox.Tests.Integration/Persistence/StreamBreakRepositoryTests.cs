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

    // A scanner that takes the lease again before any new event writes a break from the same last event: the break grows to the later end.
    [Test]
    public async Task Break_from_the_same_moment_keeps_the_later_end()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        var repository = new StreamBreakRepository(dataSource);
        var lostAt = DateTimeOffset.Parse("2026-11-08T03:10:05Z", CultureInfo.InvariantCulture);
        await repository.AddAsync(new StreamBreak("liquidations", lostAt, lostAt.AddMinutes(2)), CancellationToken.None);

        await repository.AddAsync(new StreamBreak("liquidations", lostAt, lostAt.AddMinutes(9)), CancellationToken.None);
        await repository.AddAsync(new StreamBreak("liquidations", lostAt, lostAt.AddMinutes(5)), CancellationToken.None);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        (await connection.QuerySingleAsync<DateTime>("SELECT restored_at FROM stream_breaks")).ShouldBe(lostAt.AddMinutes(9).UtcDateTime);
    }
}