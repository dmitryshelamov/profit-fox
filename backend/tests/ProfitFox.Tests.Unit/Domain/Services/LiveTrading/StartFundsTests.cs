using ProfitFox.Domain.Services.LiveTrading;
using ProfitFox.Domain.ValueObjects.LiveTrading;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.LiveTrading;

public class StartFundsTests
{
    [Test]
    public void Free_money_that_covers_the_bot_is_enough()
    {
        StartFunds.Enough(free: 1000m, balance: 1000m, others: []).ShouldBeTrue();
    }

    // Scenario "Денег не хватает с учётом другого бота".
    [Test]
    public void Other_bots_of_the_account_take_their_balance()
    {
        StartFunds.Enough(free: 1500m, balance: 1000m, others: [new BotFunds(1000m, 0m)]).ShouldBeFalse();
    }

    // What another bot has invested is already off the free money of the account.
    [Test]
    public void Other_bots_take_only_what_they_have_not_invested()
    {
        StartFunds.Enough(free: 1500m, balance: 1000m, others: [new BotFunds(1000m, 600m)]).ShouldBeTrue();
    }

    [Test]
    public void A_bot_invested_beyond_its_balance_takes_nothing_more()
    {
        StartFunds.Enough(free: 1000m, balance: 1000m, others: [new BotFunds(1000m, 1200m)]).ShouldBeTrue();
    }
}