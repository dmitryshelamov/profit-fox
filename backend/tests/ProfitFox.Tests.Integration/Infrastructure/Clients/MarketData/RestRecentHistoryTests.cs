using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.MarketData;

/// <summary>
///     Change add-ideas-and-market, task 5.4: spec market-live-feed «История для прогрева» over the REST of the fake
///     market-data service.
/// </summary>
public sealed class RestRecentHistoryTests
{
    private static readonly DateTimeOffset Midnight = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task At_half_past_midnight_the_candles_of_the_day_before_come_up_to_its_end()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var history = new RestRecentHistory(server.Http);

        RecentRead<HistoryCandle> read = await history.Candles("btc", TimeSpan.FromHours(1), Midnight.AddMinutes(30), 200, CancellationToken.None);

        Assert.That(read.Loading, Is.False);
        Assert.That(read.Items, Has.Count.EqualTo(200));
        Assert.That(read.Items[^1].OpenTime, Is.EqualTo(Midnight.AddHours(-1)), "the last candle of the day before, which History does not give yet");
        Assert.That(read.Items.Select(candle => candle.OpenTime), Is.Ordered.And.Unique);
        Assert.That(server.Data.RestRequests, Is.EqualTo(new[] { "candles btc 1h" }));
    }

    [Test]
    public async Task The_payments_and_the_open_interest_of_the_pair()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var history = new RestRecentHistory(server.Http);

        RecentRead<HistoryPayment> payments = await history.Funding("btc", Midnight.AddDays(-1), Midnight.AddMinutes(30), CancellationToken.None);
        RecentRead<OpenInterestPoint> interest = await history.OpenInterest("btc", TimeSpan.FromHours(1), Midnight, 3, CancellationToken.None);

        Assert.That(payments.Items.Select(payment => payment.Time), Is.EqualTo(new[] { Midnight.AddDays(-1), Midnight.AddHours(-16), Midnight.AddHours(-8), Midnight }));
        Assert.That(interest.Items, Is.EqualTo(new[] { new OpenInterestPoint(Midnight.AddHours(-3), 24_000), new OpenInterestPoint(Midnight.AddHours(-2), 24_000), new OpenInterestPoint(Midnight.AddHours(-1), 24_000) }));
    }

    [Test]
    public async Task A_pair_whose_history_is_loading_is_still_loading()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.Loading.Add("sol");
        var history = new RestRecentHistory(server.Http);

        RecentRead<HistoryCandle> read = await history.Candles("sol", TimeSpan.FromMinutes(15), Midnight, 200, CancellationToken.None);

        Assert.That((read.Loading, read.Items.Count), Is.EqualTo((true, 0)));
    }
}
