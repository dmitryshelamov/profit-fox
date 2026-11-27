using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData.Enums;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;

/// <summary>
///     Change add-ideas-and-market, task 6.4: the snapshot of the market is written whole in one statement.
/// </summary>
[NonParallelizable]
public sealed class MarketPairRepositoryTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 14, 32, 0, TimeSpan.Zero);

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
    }

    [Test]
    public async Task The_831_pairs_are_written_again_and_a_pair_that_left_the_set_goes()
    {
        IMarketPairRepository pairs = BenchServices.Get<IMarketPairRepository>();
        MarketPair[] first = [.. Enumerable.Range(0, 831).Select(index => Pair($"P{index:D3}USDT", price: 1 + index, Now))];
        await pairs.ReplaceAsync(first, CancellationToken.None);

        MarketPair[] second = [.. first.Skip(1).Select(pair => pair with { Price = pair.Price * 2, UpdatedAt = Now.AddMinutes(1) })];
        await pairs.ReplaceAsync(second, CancellationToken.None);

        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM market_pairs"), Is.EqualTo(830));
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM market_pairs WHERE symbol = 'P000USDT'"), Is.Zero);
        Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM market_pairs WHERE updated_at = @Now", new { Now = Now.AddMinutes(1) }), Is.EqualTo(830));
        Assert.That(await connection.ExecuteScalarAsync<decimal>("SELECT price FROM market_pairs WHERE symbol = 'P830USDT'"), Is.EqualTo(1662m));
        Assert.That(await connection.ExecuteScalarAsync<decimal?>("SELECT change_24h FROM market_pairs WHERE symbol = 'P001USDT'"), Is.Null);
    }

    /// <summary>
    ///     Change add-instrument-kind-filter, task 3.2: the kind of a pair is written with the snapshot and read back, and the
    ///     next snapshot writes the new kind (spec market-overview «Вид пары», scenario «Тип сменился»).
    /// </summary>
    [Test]
    public async Task The_kind_of_a_pair_is_written_and_read_with_the_snapshot()
    {
        IMarketPairRepository pairs = BenchServices.Get<IMarketPairRepository>();
        MarketPair btc = Pair("BTCUSDT", 97_000m, Now);
        MarketPair tsla = Pair("TSLAUSDT", 412.5m, Now) with { Kind = InstrumentKind.Stock };
        MarketPair coin = Pair("NEWUSDT", 0.5m, Now);
        await pairs.ReplaceAsync([btc, tsla, coin], CancellationToken.None);

        Assert.That((await pairs.FindAsync("TSLAUSDT", CancellationToken.None))!.Pair, Is.EqualTo(tsla));
        Assert.That((await pairs.FindAsync("BTCUSDT", CancellationToken.None))!.Pair.Kind, Is.EqualTo(InstrumentKind.Crypto));

        await pairs.ReplaceAsync([btc, tsla, coin with { Kind = InstrumentKind.Stock, UpdatedAt = Now.AddMinutes(1) }], CancellationToken.None);

        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        Assert.That(await connection.QueryAsync<(string, string)>("SELECT symbol, kind FROM market_pairs ORDER BY symbol"),
            Is.EqualTo(new[] { ("BTCUSDT", "crypto"), ("NEWUSDT", "stock"), ("TSLAUSDT", "stock") }));
        ListPage<MarketPairRow> page = await pairs.ListAsync(new MarketListQuery(new ListOrder("symbol", false, 1, 50)), CancellationToken.None);
        Assert.That(page.Items.Select(row => (row.Pair.Symbol, row.Pair.Kind)),
            Is.EqualTo(new[] { ("BTCUSDT", InstrumentKind.Crypto), ("NEWUSDT", InstrumentKind.Stock), ("TSLAUSDT", InstrumentKind.Stock) }));
    }

    private static MarketPair Pair(string symbol, decimal price, DateTimeOffset at)
    {
        return new MarketPair(symbol, price, 0.01m, 0.02m, null, 1_000_000m, 24_000m, 24_000m * price, 0.0001m, at.AddHours(2), at);
    }
}
