using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.LiveTrading;
using ProfitFox.Domain.ValueObjects.LiveTrading;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.LiveTrading;

public class ReconciliationTests
{
    private const decimal Step = 0.001m;
    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    // Scenario "Позиция совпадает".
    [Test]
    public void Linear_position_is_the_sum_of_the_bots()
    {
        Check(MarketCategory.Linear, 0.5m, Long(A, 0.3m), Long(B, 0.2m)).ShouldBeEmpty();
    }

    // Scenario "Лонг и шорт на одном символе".
    [Test]
    public void Short_counts_against_a_long_on_one_symbol()
    {
        Check(MarketCategory.Linear, 0.2m, Long(A, 0.3m), new BotHolding(B, StrategyDirection.Short, 0.1m, [])).ShouldBeEmpty();
    }

    // Scenario "Позицию закрыли вручную".
    [Test]
    public void Linear_position_that_differs_is_named()
    {
        Check(MarketCategory.Linear, 0m, Long(A, 0.5m)).ShouldBe(["Exchange position 0 does not match the positions of the bots 0.5."]);
    }

    [Test]
    public void Difference_below_a_quantity_step_is_no_difference()
    {
        Check(MarketCategory.Linear, 0.5004m, Long(A, 0.5m)).ShouldBeEmpty();
    }

    // Scenario "Лишние монеты на споте".
    [Test]
    public void Spot_coins_beyond_the_bots_are_fine()
    {
        Check(MarketCategory.Spot, 0.4m, Long(A, 0.1m)).ShouldBeEmpty();
    }

    [Test]
    public void Spot_coins_below_the_bots_are_named()
    {
        Check(MarketCategory.Spot, 0.05m, Long(A, 0.1m)).ShouldBe(["Exchange holds 0.05 of the coin, less than the positions of the bots 0.1."]);
    }

    // Scenario "Пропал ордер бота": an order neither open nor finished on the exchange.
    [Test]
    public void Saved_order_the_exchange_does_not_know_is_named()
    {
        var holding = new BotHolding(A, StrategyDirection.Long, 0.5m, [3, 4]);
        var exchange = new ExchangeHoldings(0.5m, [new ClientOrderId(A, 3)], [new ClientOrderId(A, 1)]);

        Reconciliation.Check(MarketCategory.Linear, Step, [holding], exchange)
            .ShouldBe([$"Order {new ClientOrderId(A, 4).Code} of the bot is not on the exchange."]);
    }

    [Test]
    public void Saved_order_that_finished_meanwhile_is_known()
    {
        var holding = new BotHolding(A, StrategyDirection.Long, 0.5m, [4]);
        var exchange = new ExchangeHoldings(0.5m, [], [new ClientOrderId(A, 4)]);

        Reconciliation.Check(MarketCategory.Linear, Step, [holding], exchange).ShouldBeEmpty();
    }

    // Scenario "Неизвестный ордер бота".
    [Test]
    public void Open_order_of_a_bot_that_it_does_not_hold_is_named()
    {
        var holding = new BotHolding(A, StrategyDirection.Long, 0.5m, []);
        var exchange = new ExchangeHoldings(0.5m, [new ClientOrderId(A, 9)], []);

        Reconciliation.Check(MarketCategory.Linear, Step, [holding], exchange)
            .ShouldBe([$"Order {new ClientOrderId(A, 9).Code} of the bot is unknown."]);
    }

    // An open order of a bot that is not reconciled here, of another instrument, is not this check's business.
    [Test]
    public void Open_order_of_another_bot_is_left_alone()
    {
        var exchange = new ExchangeHoldings(0.5m, [new ClientOrderId(B, 9)], []);

        Reconciliation.Check(MarketCategory.Linear, Step, [Long(A, 0.5m)], exchange).ShouldBeEmpty();
    }

    private static IReadOnlyList<string> Check(MarketCategory category, decimal exchange, params BotHolding[] bots)
    {
        return Reconciliation.Check(category, Step, bots, new ExchangeHoldings(exchange, [], []));
    }

    private static BotHolding Long(Guid bot, decimal quantity)
    {
        return new BotHolding(bot, StrategyDirection.Long, quantity, []);
    }
}