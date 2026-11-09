using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Grpc.Core;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>
///     Group 10: a trading Worker keeps a bot of the ladder on <c>moex</c> / <c>SBER</c> / <c>60</c> with a T-Invest account on
///     <see cref="FakeTInvest" />, through the adapter of T-Invest as it is, from a Monday evening: the evening session ends at 20:50 UTC,
///     the morning auction of the next day opens at 03:50 UTC. The lease of a bot is an hour here, so the clock may run through the night in
///     steps of a minute; the streams are pinged at each step, as T-Invest pings them.
/// </summary>
public class MoexLiveTradingTests
{
    private const string Token = "t.token";
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-11T18:30:00Z");
    private static readonly DateTimeOffset DayEnd = DateTimeOffset.Parse("2024-03-11T20:50:00Z");
    private static readonly DateTimeOffset Opening = DateTimeOffset.Parse("2024-03-12T03:50:00Z");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private FakeTInvest _tinvest = null!;
    private WorkerHost _worker = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = 0, ApiSettings = new Dictionary<string, string?> { ["TInvest:Trading"] = "true" } };
        _factory.Time.SetUtcNow(Start);
        _tinvest = new FakeTInvest(
            _factory.TInvest,
            _factory.Time,
            [
                ("regular_trading_session_main", DateTimeOffset.Parse("2024-03-11T07:00:00Z"), DateTimeOffset.Parse("2024-03-11T15:40:00Z")),
                ("regular_trading_session_evening", DateTimeOffset.Parse("2024-03-11T16:05:00Z"), DayEnd),
                ("opening_auction_morning", Opening, Opening.AddMinutes(10)),
                ("regular_trading_session_morning", Opening.AddMinutes(10), DateTimeOffset.Parse("2024-03-12T06:50:00Z"))
            ]);
        _factory.BrokerAccounts.ListAccountsAsync(Token, Arg.Any<CancellationToken>())
            .Returns([new BrokerAccount(FakeTInvest.AccountNumber, "Брокерский счёт", BrokerAccountType.Brokerage, BrokerAccountStatus.Open, BrokerAccountAccess.Full)]);
        _factory.Ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), "RUB", Arg.Any<CancellationToken>()).Returns(1_000_000m);
        _client = _factory.CreateClient();
        _worker = _factory.StartWorker(workerSettings: new Dictionary<string, string?>
        {
            ["Trading:Enabled"] = "true",
            ["Trading:LeaseDuration"] = "01:00:00",
            ["Trading:LeaseRenewal"] = "00:10:00",
            ["ExchangeAccounts:MasterKey"] = ProfitFoxFactory.TestMasterKey
        });
    }

    [TearDown]
    public async Task TearDown()
    {
        await _worker.DisposeAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Task 10.1, scenarios "Лимитка пережила ночь", "Заявка ночью" and "Пауза до открытия сессии": the closed candle of the stream reaches
    // the bot, the limits of the day go after the fill of the entry, the exchange takes them off at the end of the day, the ladder places
    // them again, and they wait for the morning.
    [Test]
    public async Task Limits_of_the_day_taken_off_at_night_go_again_when_the_next_session_opens()
    {
        Guid bot = await StartedAndFilledAsync(commission: 4.5m);
        var hour = DateTimeOffset.Parse("2024-03-11T18:00:00Z");
        _tinvest.CloseCandle(hour, 300m);
        await WaitAsync(async () => (await LiveAsync(bot)).LastBarAt == hour, "the closed candle of the stream");
        await WaitAsync(() => _tinvest.Limits.Count == 2, "the limits of the day");
        _tinvest.Posted.Where(post => post.Request.OrderType == OrderType.Limit).ShouldAllBe(post => post.Request.TimeInForce == TimeInForceType.TimeInForceDay);
        foreach (string limit in _tinvest.Limits)
        {
            _tinvest.Tell(limit);
        }

        await AdvanceToAsync(DayEnd);
        int posted = _tinvest.Posted.Count;
        _tinvest.ExpireDayOrders();

        await WaitAsync(async () => (await OrdersAsync(bot)).Count(order => order is { Status: LiveOrderStatus.PendingSubmit, NextAttemptAt: not null }) == 2, "the limits placed again for the morning");
        (await OrdersAsync(bot)).Where(order => order.Status == LiveOrderStatus.PendingSubmit).ShouldAllBe(order => order.NextAttemptAt == Opening);
        (await OrdersAsync(bot)).Count(order => order.Status == LiveOrderStatus.Cancelled).ShouldBe(2);
        await AdvanceToAsync(Opening.AddSeconds(-1));
        _tinvest.Posted.Count.ShouldBe(posted);

        await AdvanceToAsync(Opening.AddMinutes(1));

        await WaitAsync(() => _tinvest.Posted.Count == posted + 2, "the limits sent in the morning");
        _tinvest.Posted.Skip(posted).ShouldAllBe(post => post.At >= Opening && post.Request.TimeInForce == TimeInForceType.TimeInForceDay);
        _tinvest.Posted.ShouldNotContain(post => post.At > DayEnd && post.At < Opening);
        (await LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
    }

    // Task 10.2, scenario "Комиссия пришла позже исполнения": the fill comes with no commission, which shows 10 s later.
    [Test]
    public async Task Commission_that_comes_after_the_fill_reaches_the_position()
    {
        Guid bot = await StartedAndFilledAsync(commission: 0m, late: 1.25m, after: TimeSpan.FromSeconds(10));

        await WaitAsync(async () => (await PositionsAsync(bot)).FirstOrDefault() is { Quantity: 30m }, "the position");

        LivePosition position = (await PositionsAsync(bot)).Single();
        position.Fees.ShouldBe(1.25m);
        (await OrdersAsync(bot)).Single(order => order.Number == 1).Fee.ShouldBe(1.25m);
    }

    // Task 10.3, scenario "Заявка бота неизвестна бирже": after the stream of orders is back, the books are compared with the exchange,
    // which no longer knows an active limit of the bot.
    [Test]
    public async Task Active_order_the_exchange_does_not_know_after_a_restore_fails_the_bot()
    {
        Guid bot = await StartedAndFilledAsync(commission: 4.5m);
        await WaitAsync(async () => (await OrdersAsync(bot)).Count(order => order.Status == LiveOrderStatus.Active) == 2, "the active limits");
        LiveOrder forgotten = (await OrdersAsync(bot)).Single(order => order.Status == LiveOrderStatus.Active && order.ExchangeOrderId == $"R-{_tinvest.Limits[0][..8]}");
        _tinvest.Forget(_tinvest.Limits[0]);

        _tinvest.OrderStream.Fail(StatusCode.Unavailable);

        await WaitAsync(async () => (await LiveAsync(bot)).Status == LiveBotStatus.Failed, "the failed bot");
        (await LiveAsync(bot)).StopReason.ShouldBe($"Order {forgotten.ClientId.Code} of the bot is not on the exchange.");

        // The books of a bot of moex are compared with the bots and the trades of its share, never with the coins of the spot of Bybit.
        _factory.Bybit.ListRequests.ShouldBeEmpty();
    }

    /// <summary>
    ///     A bot of the ladder on SBER, started, whose entry by amount (30 shares at the close of 300) is filled whole at 300 with the
    ///     commission given, once its closed candles are subscribed.
    /// </summary>
    private async Task<Guid> StartedAndFilledAsync(decimal commission, decimal late = 0m, TimeSpan? after = null)
    {
        HttpResponseMessage account = await _client.PostAsJsonAsync("/api/exchange-accounts", new { name = "Брокерский", exchange = "tinvest", token = Token, accountId = FakeTInvest.AccountNumber });
        account.StatusCode.ShouldBe(HttpStatusCode.Created, await account.Content.ReadAsStringAsync());
        Guid accountId = (await account.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new { name = "Ladder", definition = StrategyFixtures.Element("ladder") });
        Guid strategyId = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage created = await _client.PostAsJsonAsync(
            "/api/bots",
            new JsonObject
            {
                ["name"] = "SBER Ladder",
                ["strategyId"] = strategyId,
                ["parameterValues"] = new JsonObject { ["limitPerOrder"] = 10000, ["limitDeposit"] = 30000 },
                ["category"] = "moex",
                ["symbol"] = "SBER",
                ["interval"] = "60",
                ["balance"] = 50000,
                ["exchangeAccountId"] = accountId
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid bot = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage started = await _client.PostAsync($"/api/bots/{bot}/start", null);
        started.StatusCode.ShouldBe(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());

        await WaitAsync(() => _tinvest.Posted.Count == 1, "the entry");
        PostOrderRequest entry = _tinvest.Posted[0].Request;
        (entry.OrderType, entry.Quantity, entry.AccountId).ShouldBe((OrderType.Market, 3L, FakeTInvest.AccountNumber));
        await WaitAsync(() => _tinvest.ClosedCandlesSubscribed && _tinvest.OrderStream.IsOpen, "the streams");
        _tinvest.Fill(entry.OrderId, 300m, commission, late, after);

        return bot;
    }

    /// <summary>Moves the clock a second at a time until the condition holds, pinging the streams, so the Worker polls and the streams stay.</summary>
    private async Task WaitAsync(Func<Task<bool>> condition, string what)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            if (await condition())
            {
                return;
            }

            _factory.Time.Advance(TimeSpan.FromSeconds(1));
            _tinvest.Ping();
            await Task.Delay(25);
        }

        throw new TimeoutException($"Waited in vain for {what}.");
    }

    private Task WaitAsync(Func<bool> condition, string what)
    {
        return WaitAsync(() => Task.FromResult(condition()), what);
    }

    /// <summary>Moves the clock a minute at a time up to the moment, pinging the streams.</summary>
    private async Task AdvanceToAsync(DateTimeOffset moment)
    {
        while (_factory.Time.GetUtcNow() < moment)
        {
            TimeSpan left = moment - _factory.Time.GetUtcNow();
            _factory.Time.Advance(left < TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1));
            _tinvest.Ping();
            await Task.Delay(25);
        }

        await Task.Delay(200);
    }

    private async Task<LiveBot> LiveAsync(Guid bot)
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ILiveBotRepository>().GetAsync(bot, CancellationToken.None);
    }

    private async Task<IReadOnlyList<LiveOrder>> OrdersAsync(Guid bot)
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ILiveBotRepository>().GetOrdersAsync(bot, CancellationToken.None);
    }

    private async Task<IReadOnlyList<LivePosition>> PositionsAsync(Guid bot)
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ILiveBotRepository>().GetPositionsAsync(bot, CancellationToken.None);
    }
}