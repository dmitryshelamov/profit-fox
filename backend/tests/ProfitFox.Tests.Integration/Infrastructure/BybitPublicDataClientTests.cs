using System.Globalization;

using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Exchanges;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Retry;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Decision 6: the public data of the scanner from Bybit v5, through the fake Bybit.</summary>
public class BybitPublicDataClientTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-01T00:00:00Z", CultureInfo.InvariantCulture);

    private FakeBybitHandler _bybit = null!;
    private IScannerStateRepository _state = null!;
    private ScannerRestGate _gate = null!;
    private BybitPublicDataClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _bybit = new FakeBybitHandler();
        _state = Substitute.For<IScannerStateRepository>();
        _state.GetAsync(Arg.Any<CancellationToken>()).Returns(new ScannerState(null, 0, 0, null));
        var time = new FakeTimeProvider(Start);
        _gate = new ScannerRestGate(new ScannerSettings { RestRequestsPerSecond = 1000 }, new ExchangeResilienceExecutor(time), _state, time);
        _client = new BybitPublicDataClient(
            new BybitRestClient(new HttpClient(_bybit, disposeHandler: false), NullLoggerFactory.Instance, Options.Create(new BybitRestOptions { RateLimiterEnabled = false })),
            _gate);
    }

    [TearDown]
    public void TearDown()
    {
        _gate.Dispose();
        _bybit.Dispose();
    }

    // Scenario "Пара spot": a spot ticker has no open interest and no funding.
    [Test]
    public async Task Spot_tickers_are_parsed_exactly_without_open_interest_and_funding()
    {
        _bybit.Tickers[("spot", "BTCUSDT")] = ("64000.5", "64000.25", "64000.75", "", "");

        IReadOnlyList<MarketTicker> tickers = await _client.GetTickersAsync(MarketCategory.Spot, CancellationToken.None);

        tickers.ShouldBe([new MarketTicker(new Instrument(MarketCategory.Spot, "BTCUSDT"), 64000.5m, 64000.25m, 64000.75m)]);
        _bybit.TickerRequests.ShouldBe(["spot"]);
    }

    [Test]
    public async Task Linear_tickers_carry_open_interest_in_coins_and_the_funding_rate()
    {
        _bybit.Tickers[("linear", "SOLUSDT")] = ("150.125", "150.12", "150.13", "1234567.8", "-0.00012345");

        IReadOnlyList<MarketTicker> tickers = await _client.GetTickersAsync(MarketCategory.Linear, CancellationToken.None);

        tickers.ShouldBe([new MarketTicker(new Instrument(MarketCategory.Linear, "SOLUSDT"), 150.125m, 150.12m, 150.13m, 1234567.8m, -0.00012345m)]);
    }

    [Test]
    public async Task Open_interest_follows_the_cursor_through_every_page()
    {
        _bybit.OpenInterestPageSize = 2;
        _bybit.OpenInterest.AddRange(Enumerable.Range(0, 5).Select(i => ("SOLUSDT", Start.AddMinutes(5 * i), $"1000.{i}5")));
        _bybit.OpenInterest.Add(("BTCUSDT", Start, "1"));

        IReadOnlyList<OpenInterestPoint> points = await _client.GetOpenInterestAsync("SOLUSDT", Start, Start.AddMinutes(25), CancellationToken.None);

        points.ShouldBe(Enumerable.Range(0, 5).Select(i => new OpenInterestPoint(Start.AddMinutes(5 * i), decimal.Parse($"1000.{i}5", CultureInfo.InvariantCulture))).ToList());
        _bybit.OpenInterestRequests.Count.ShouldBe(3);
        _bybit.OpenInterestRequests.ShouldAllBe(query => query.Contains("intervalTime=5min") && query.Contains("limit=200"));
    }

    [Test]
    public async Task Open_interest_at_the_end_of_the_period_is_left_out()
    {
        _bybit.OpenInterest.Add(("SOLUSDT", Start, "1"));
        _bybit.OpenInterest.Add(("SOLUSDT", Start.AddMinutes(5), "2"));

        IReadOnlyList<OpenInterestPoint> points = await _client.GetOpenInterestAsync("SOLUSDT", Start, Start.AddMinutes(5), CancellationToken.None);

        points.ShouldBe([new OpenInterestPoint(Start, 1m)]);
    }

    [Test]
    public async Task Funding_history_pages_back_in_time_through_the_period()
    {
        _bybit.FundingRates.AddRange(Enumerable.Range(0, 450).Select(i => ("SOLUSDT", Start.AddHours(8 * i), $"0.0001{i:000}")));

        IReadOnlyList<FundingRatePoint> rates = await _client.GetFundingHistoryAsync("SOLUSDT", Start, Start.AddHours(8 * 450), CancellationToken.None);

        rates.Count.ShouldBe(450);
        rates[0].ShouldBe(new FundingRatePoint(Start, 0.0001000m));
        rates[^1].ShouldBe(new FundingRatePoint(Start.AddHours(8 * 449), 0.0001449m));
        rates.Select(rate => rate.Time).ShouldBeInOrder();
        _bybit.FundingRequests.Count.ShouldBe(3);
    }

    [Test]
    public async Task Minute_candles_of_the_period_come_in_one_request_oldest_first()
    {
        _bybit.Klines.Add(["2024-03-01T00:00:00Z", "100.5", "101.25", "99.75", "100.125", "12.5", "1250.75"]);
        _bybit.Klines.Add(["2024-03-01T00:01:00Z", "100.125", "102", "100", "101", "7", "700"]);
        _bybit.Klines.Add(["2024-03-01T00:02:00Z", "1", "1", "1", "1", "1", "1"]);

        IReadOnlyList<Candle> candles = await _client.GetMinuteCandlesAsync(new Instrument(MarketCategory.Linear, "SOLUSDT"), Start, Start.AddMinutes(2), CancellationToken.None);

        candles.ShouldBe(
        [
            new Candle(Start, new Price(100.5m), new Price(101.25m), new Price(99.75m), new Price(100.125m), new Volume(12.5m), new Volume(1250.75m)),
            new Candle(Start.AddMinutes(1), new Price(100.125m), new Price(102m), new Price(100m), new Price(101m), new Volume(7m), new Volume(700m))
        ]);
        _bybit.KlineRequests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Instruments_are_listed_with_their_trading_and_contract()
    {
        _bybit.Instruments.Add(("linear", "SOLUSDT"));
        _bybit.Coins[("linear", "SOLUSDT")] = ("SOL", "USDT");

        IReadOnlyList<ListedInstrument> listed = await _client.ListInstrumentsAsync(MarketCategory.Linear, CancellationToken.None);

        listed.ShouldBe([new ListedInstrument(MarketCategory.Linear, "SOLUSDT", "SOL", "USDT", Trading: true, Perpetual: true, ListedAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))]);
    }

    // Scenarios "Bybit ответил 403" and "Bybit ответил 10006": only a ban of the IP stops the requests for the whole pause.
    [TestCase("403", 600)]
    [TestCase("429", 30)]
    [TestCase("10006", 30)]
    public async Task Only_a_ban_of_the_ip_pauses_the_requests_for_the_rest_pause(string answer, int seconds)
    {
        _bybit.RateLimit = answer;

        await Should.ThrowAsync<ExchangeRateLimitedException>(() =>
            _client.GetMinuteCandlesAsync(new Instrument(MarketCategory.Linear, "SOLUSDT"), Start, Start.AddMinutes(1), CancellationToken.None));

        await _state.Received(1).PauseRestAsync(Start.AddSeconds(seconds), Arg.Any<CancellationToken>());
    }
}