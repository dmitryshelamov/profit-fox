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
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Bots;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.LiveTrading;

public class StartBotRaceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    // The exchange checked the bot as it was; a change saved before the lock was taken must not start unchecked.
    [Test]
    public async Task Bot_changed_while_starting_does_not_start()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Now);
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Spot }, Now);
        Bot bot = library.AddBot(
            Guid.NewGuid(), "BTC EMA", ema.Id, new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneHour, 1000m, Now, new BotAccount(account.Id, account.Categories));
        IStrategyLibraryRepository repository = Substitute.For<IStrategyLibraryRepository>();
        repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(library);
        repository.GetBotsAsync(Arg.Any<CancellationToken>()).Returns([bot]);
        repository.GetBotAsync(bot.Id, Arg.Any<CancellationToken>()).Returns(bot with { Balance = 2000m, UpdatedAt = Now.AddMinutes(1) });
        IExchangeAccountRepository accounts = Substitute.For<IExchangeAccountRepository>();
        accounts.GetAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        accounts.GetSecretAsync(account.Id, Arg.Any<CancellationToken>()).Returns(new EncryptedSecret([1], [2], [3]));
        ISecretProtector protector = Substitute.For<ISecretProtector>();
        protector.Decrypt(account.Id, Arg.Any<EncryptedSecret>()).Returns("secret");
        IExchangeKeyInspector inspector = Substitute.For<IExchangeKeyInspector>();
        inspector.InspectAsync("key", "secret", Arg.Any<CancellationToken>()).Returns(new ExchangeKeyInfo(100, false, true, false, account.Categories));
        IExchangeLedger ledger = Substitute.For<IExchangeLedger>();
        ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), "USDT", Arg.Any<CancellationToken>()).Returns(5000m);
        IInstrumentCatalog catalog = Substitute.For<IInstrumentCatalog>();
        catalog.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Spot, "BTCUSDT", "BTC", "USDT")]);
        ILiveBotRepository live = Substitute.For<ILiveBotRepository>();
        live.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, LiveBot>());
        live.LockAsync(bot.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(LiveBot.New(bot.Id));
        ITransactionExecutor executor = Substitute.For<ITransactionExecutor>();
        executor.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<int>>>(0)(CancellationToken.None));
        var handler = new StartBotCommandHandler(repository, accounts, protector, inspector, ledger, catalog, live, Substitute.For<IIdeaTradeRepository>(), executor, new FakeTimeProvider(Now));

        await Should.ThrowAsync<ConflictException>(() => handler.Handle(new StartBotCommand(bot.Id), CancellationToken.None));

        await live.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default);
    }
}