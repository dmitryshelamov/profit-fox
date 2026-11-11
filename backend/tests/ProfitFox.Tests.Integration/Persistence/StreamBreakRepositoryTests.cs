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

    // Decision 9 of collect-trade-flow: the breaks of a stream that overlap a period, of no other stream.
    [Test]
    public async Task Breaks_of_a_stream_that_overlap_the_period_are_read()
    {
        await using NpgsqlDataSource dataSource = MarketDataDatabase.Open();
        var repository = new StreamBreakRepository(dataSource);
        var from = DateTimeOffset.Parse("2026-11-10T14:00:00Z", CultureInfo.InvariantCulture);
        var before = new StreamBreak("trades", from.AddMinutes(-10), from.AddMinutes(-1));
        var across = new StreamBreak("trades", from.AddMinutes(-1), from.AddMinutes(1));
        var inside = new StreamBreak("trades", from.AddMinutes(30), from.AddMinutes(31));
        var after = new StreamBreak("trades", from.AddHours(1), from.AddHours(1).AddMinutes(1));

        foreach (StreamBreak streamBreak in new[] { before, across, inside, after, new StreamBreak("liquidations", from.AddMinutes(5), from.AddMinutes(6)) })
        {
            await repository.AddAsync(streamBreak, CancellationToken.None);
        }

        (await repository.GetAsync("trades", from, from.AddHours(1), CancellationToken.None)).ShouldBe([across, inside], ignoreOrder: true);
    }
}