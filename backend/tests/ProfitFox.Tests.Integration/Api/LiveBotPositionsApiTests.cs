using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class LiveBotPositionsApiTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0 };
        _factory.Bybit.Instruments.Add(("spot", "BTCUSDT"));
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Позиции после закрытия".
    [Test]
    public async Task Positions_come_the_newest_first_with_their_orders_and_fills()
    {
        Guid bot = await CreateBotAsync();
        await InUnitAsync(async (repository, cancellationToken) =>
        {
            LiveBot live = (await repository.LockAsync(bot, Now, cancellationToken))!;
            await repository.SaveAsync(live.Start(1, new Dictionary<string, NodeValue>()), Now, cancellationToken);
            await repository.SavePositionAsync(new LivePosition(bot, 1, false, false, 0m, 0m, 0m, 5m, 0.2m, 100m, Now, 0, Now.AddHours(1)), cancellationToken);
            await repository.SavePositionAsync(new LivePosition(bot, 2, false, false, 0m, 0m, 0m, -1m, 0.2m, 100m, Now.AddHours(2), 0, Now.AddHours(3)), cancellationToken);
            await repository.SavePositionAsync(new LivePosition(bot, 3, true, false, 0.5m, 50m, 50m, 0m, 0.05m, 100m, Now.AddHours(4), 0, null), cancellationToken);
            await repository.SaveOrderAsync(Order(bot, 1, 1, OrderSide.Buy, LiveOrderStatus.Filled, 1m), cancellationToken);
            await repository.SaveOrderAsync(Order(bot, 2, 1, OrderSide.Sell, LiveOrderStatus.Filled, 1m), cancellationToken);
            await repository.SaveOrderAsync(Order(bot, 5, 3, OrderSide.Buy, LiveOrderStatus.Filled, 0.5m), cancellationToken);
            await repository.SaveOrderAsync(Order(bot, 6, 3, OrderSide.Sell, LiveOrderStatus.Active, 0m) with { Price = 101m }, cancellationToken);
            await repository.AddFillAsync(new LiveFill("e-1", bot, 1, 1m, 100m, 0.1m, FeeAsset.Quote, Now), cancellationToken);
            await repository.AddFillAsync(new LiveFill("e-2", bot, 2, 1m, 105m, 0.1m, FeeAsset.Quote, Now.AddHours(1)), cancellationToken);
            await repository.AddFillAsync(new LiveFill("e-5", bot, 5, 0.5m, 100m, 0.05m, FeeAsset.Quote, Now.AddHours(4)), cancellationToken);
        });

        JsonElement positions = await _client.GetFromJsonAsync<JsonElement>($"/api/bots/{bot}/positions");

        positions.EnumerateArray().Select(position => position.GetProperty("number").GetInt32()).ShouldBe([3, 2, 1]);
        JsonElement current = positions[0];
        (current.GetProperty("status").GetString(), current.GetProperty("quantity").GetString(), current.GetProperty("invested").GetString())
            .ShouldBe(("open", "0.5", "50"));
        current.GetProperty("openedAt").GetDateTimeOffset().ShouldBe(Now.AddHours(4));
        current.GetProperty("orders").EnumerateArray().Select(order => (order.GetProperty("status").GetString(), order.GetProperty("fills").GetArrayLength()))
            .ShouldBe([("filled", 1), ("active", 0)]);
        JsonElement first = positions[2];
        (first.GetProperty("status").GetString(), first.GetProperty("entryPrice").GetString(), first.GetProperty("exitPrice").GetString(), first.GetProperty("pnl").GetString())
            .ShouldBe(("closed", "100", "105", "5"));
    }

    [Test]
    public async Task Position_reads_as_closing_and_then_as_waiting()
    {
        Guid bot = await CreateBotAsync();
        await InUnitAsync(async (repository, cancellationToken) =>
        {
            LiveBot live = (await repository.LockAsync(bot, Now, cancellationToken))!;
            await repository.SaveAsync(live.Start(1, new Dictionary<string, NodeValue>()), Now, cancellationToken);
            await repository.SavePositionAsync(new LivePosition(bot, 1, false, true, 0.0001m, 0.01m, 0.01m, 0m, 0m, 100m, Now, 0, null), cancellationToken);
        });
        (await _client.GetFromJsonAsync<JsonElement>($"/api/bots/{bot}/positions"))[0].GetProperty("status").GetString().ShouldBe("closing");

        await InUnitAsync((repository, cancellationToken) =>
            repository.SavePositionAsync(new LivePosition(bot, 1, false, false, 0.0001m, 0.01m, 0.01m, 0m, 0m, 100m, Now, 0, null), cancellationToken));

        (await _client.GetFromJsonAsync<JsonElement>($"/api/bots/{bot}/positions"))[0].GetProperty("status").GetString().ShouldBe("waiting");
    }

    [Test]
    public async Task Bot_that_never_traded_has_no_positions()
    {
        Guid bot = await CreateBotAsync();

        (await _client.GetFromJsonAsync<JsonElement>($"/api/bots/{bot}/positions")).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Positions_of_an_unknown_bot_are_not_found()
    {
        (await _client.GetAsync($"/api/bots/{Guid.NewGuid()}/positions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static LiveOrder Order(Guid bot, int number, int position, OrderSide side, LiveOrderStatus status, decimal filled)
    {
        return new LiveOrder(bot, number, position, "node", side, OrderType.Market, null, 1m, null, 0m, status, filled, $"x-{number}", null, 0, null, Now, Now);
    }

    private async Task InUnitAsync(Func<ILiveBotRepository, CancellationToken, Task> work)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ILiveBotRepository repository = scope.ServiceProvider.GetRequiredService<ILiveBotRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>().ExecuteAsync(
            async cancellationToken =>
            {
                await work(repository, cancellationToken);

                return 0;
            },
            CancellationToken.None);
    }

    private async Task<Guid> CreateBotAsync()
    {
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new { name = "EMA", definition = StrategyFixtures.Element("ema") });
        Guid ema = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/bots",
            new { name = "BTC EMA час", strategyId = ema, category = "spot", symbol = "BTCUSDT", interval = "60", balance = 1000 });

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}