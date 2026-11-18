using Grpc.Core;
using Grpc.Net.Client;
using ProfitFox.Bench.MarketData;
using ProfitFox.Bench.MarketData.History;
using ProfitFox.Bench.MarketData.Protos;
using ProfitFox.Engine.Strategy.Execution;
using TimeRange = ProfitFox.Bench.MarketData.History.TimeRange;

namespace ProfitFox.Tests.Integration.Bench.MarketData;

/// <summary>
///     Spec market-history over gRPC: the <c>History</c> streams of a fake market-data service in the test process.
/// </summary>
public sealed class GrpcHistorySourceTests
{
    private static readonly DateTimeOffset Start = FakeMarketData.CoveredFrom;

    [Test]
    public async Task Candles_and_their_coverage_come_from_the_stream()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var source = new GrpcHistorySource(server.Channel);

        HistoryRead<HistoryCandle> read = await source.ReadCandles("btc", TimeSpan.FromHours(4), Start.AddDays(-1), Start.AddDays(1), CancellationToken.None);

        Assert.That(read.Coverage, Is.EqualTo(new[] { new TimeRange(Start, Start.AddDays(1)) }));
        Assert.That(read.Items, Has.Count.EqualTo(6));
        Assert.That(read.Items[0], Is.EqualTo(new HistoryCandle(Start, "100.5", "101", "100", "100.25", "12.5", Turnover: null, BuyVolume: "6")));
        CandleHistoryRequest request = server.Data.CandleRequests.Single();
        Assert.That(request.Interval, Is.EqualTo(CandleInterval.FourHours));
        Assert.That(request.InstrumentIds, Is.EqualTo(new[] { "btc" }));
    }

    [Test]
    public async Task Funding_payments_come_with_their_rate_as_text()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        var source = new GrpcHistorySource(server.Channel);

        HistoryRead<HistoryPayment> read = await source.ReadFunding("btc", Start, Start.AddDays(1), CancellationToken.None);

        Assert.That(read.Items.Select(payment => payment.Time), Is.EqualTo(new[] { Start, Start.AddHours(8), Start.AddHours(16) }));
        Assert.That(read.Items[0].ToFundingPayment(), Is.EqualTo(new FundingPayment(Start, 0.0005m)));
    }

    [Test]
    public async Task A_stopping_service_is_history_unavailable()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        server.Data.Failure = StatusCode.Unavailable;
        var source = new GrpcHistorySource(server.Channel);

        Assert.That(
            () => source.ReadCandles("btc", TimeSpan.FromHours(4), Start, Start.AddDays(1), CancellationToken.None),
            Throws.InstanceOf<HistoryUnavailableException>());
    }

    [Test]
    public void A_service_that_does_not_answer_is_history_unavailable()
    {
        using GrpcChannel channel = GrpcChannel.ForAddress("http://127.0.0.1:9");
        var source = new GrpcHistorySource(channel);

        Assert.That(
            () => source.ReadCandles("btc", TimeSpan.FromHours(4), Start, Start.AddDays(1), CancellationToken.None),
            Throws.InstanceOf<HistoryUnavailableException>());
    }

    [Test]
    public async Task A_run_reads_only_the_windows_it_asks_for_and_the_same_read_gives_the_same_hash()
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        string[] hashes = new string[2];
        IReadOnlyList<ClosedCandle> traded = [];
        for (int run = 0; run < 2; run++)
        {
            using var hash = new HistoryHash();
            var history = new MarketHistory(new GrpcHistorySource(server.Channel), Start.AddDays(30), hash);
            (_, traded) = await history.TradedCandles("btc", TimeSpan.FromHours(4), Start, Start.AddDays(2), CancellationToken.None);
            await history.Candles("btc", TimeSpan.FromMinutes(1), Start.AddDays(1).AddHours(3), Start.AddDays(1).AddHours(9), CancellationToken.None);
            hashes[run] = hash.Result();
        }

        Assert.That(traded, Has.Count.EqualTo(10), "the 08:00 candle of each day has no trades");
        Assert.That(server.Data.CandleRequests.Take(2).Select(request => (request.Interval, request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset())), Is.EqualTo(new[]
        {
            (CandleInterval.FourHours, Start, Start.AddDays(2)),
            (CandleInterval.OneMinute, Start.AddDays(1).AddHours(3), Start.AddDays(1).AddHours(9))
        }));
        Assert.That(hashes[1], Is.EqualTo(hashes[0]));
    }
}
