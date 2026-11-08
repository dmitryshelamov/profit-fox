using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.LiveTrading.Commands.StartBot;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Strategies;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Bots;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.LiveTrading;

/// <summary>Scenario "Бот на MOEX" and decision 10: the start is refused before any call to an exchange.</summary>
public class StartBotMoexTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    [Test]
    public async Task Start_of_a_bot_on_moex_is_a_conflict_without_asking_any_exchange()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Now);
        var account = new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, "2000000001", new HashSet<MarketCategory> { MarketCategory.Moex }, Now);
        Bot bot = library.AddBot(
            Guid.NewGuid(), "SBER EMA", ema.Id, new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Moex, "SBER"), Interval.OneHour, 10000m, Now, new BotAccount(account.Id, account.Categories));
        IStrategyLibraryRepository repository = Substitute.For<IStrategyLibraryRepository>();
        repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(library);
        IExchangeAccountRepository accounts = Substitute.For<IExchangeAccountRepository>();
        accounts.GetAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        IExchangeKeyInspector inspector = Substitute.For<IExchangeKeyInspector>();
        IExchangeLedger ledger = Substitute.For<IExchangeLedger>();
        ILiveBotRepository live = Substitute.For<ILiveBotRepository>();
        var handler = new StartBotCommandHandler(
            repository,
            accounts,
            Substitute.For<ISecretProtector>(),
            inspector,
            ledger,
            Substitute.For<IInstrumentCatalog>(),
            live,
            Substitute.For<IIdeaTradeRepository>(),
            Substitute.For<ITransactionExecutor>(),
            new FakeTimeProvider(Now));

        ConflictException exception = await Should.ThrowAsync<ConflictException>(() => handler.Handle(new StartBotCommand(bot.Id), CancellationToken.None));

        exception.Message.ShouldBe("Trading on MOEX is not supported yet.");
        inspector.ReceivedCalls().ShouldBeEmpty();
        ledger.ReceivedCalls().ShouldBeEmpty();
        live.ReceivedCalls().ShouldBeEmpty();
        await accounts.DidNotReceiveWithAnyArgs().GetSecretAsync(default, default);
    }
}