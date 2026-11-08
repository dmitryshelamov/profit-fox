using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.LiveTrading;
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
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
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

/// <summary>
///     Decisions 1 and 10: the start of a bot on moex is refused while the trading on MOEX is off, before any call to an exchange; while
///     it is on, the token must still open the account with full access and the free roubles of the account must cover the bot.
/// </summary>
public class StartBotMoexTests
{
    private const string Token = "t.token";
    private const string AccountNumber = "2000000001";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");
    private static readonly Instrument Sber = new Instrument(MarketCategory.Moex, "SBER");

    private ExchangeAccount _account = null!;
    private Bot _bot = null!;
    private IExchangeAccountRepository _accounts = null!;
    private IExchangeKeyInspector _inspector = null!;
    private IBrokerAccountReader _broker = null!;
    private IExchangeLedger _ledger = null!;
    private ILiveBotRepository _live = null!;
    private IStrategyLibraryRepository _library = null!;
    private IInstrumentCatalog _catalog = null!;
    private ITransactionExecutor _executor = null!;
    private ISecretProtector _protector = null!;

    [SetUp]
    public void SetUp()
    {
        var library = new StrategyLibrary(0, [], []);
        Strategy ema = library.AddStrategy(Guid.NewGuid(), "EMA", StrategyJson.ParseStrategy(StrategyFixtures.Element("ema")), Now);
        _account = new ExchangeAccount(Guid.NewGuid(), "Брокерский", Exchange.TInvest, null, null, AccountNumber, new HashSet<MarketCategory> { MarketCategory.Moex }, Now);
        _bot = library.AddBot(Guid.NewGuid(), "SBER EMA", ema.Id, new Dictionary<string, NodeValue>(), Sber, Interval.OneHour, 45_000m, Now, new BotAccount(_account.Id, _account.Categories));
        _library = Substitute.For<IStrategyLibraryRepository>();
        _library.LoadAsync(Arg.Any<CancellationToken>()).Returns(library);
        _library.GetBotsAsync(Arg.Any<CancellationToken>()).Returns([_bot]);
        _library.GetBotAsync(_bot.Id, Arg.Any<CancellationToken>()).Returns(_bot);
        var encrypted = new EncryptedSecret([1], [2], [3]);
        _accounts = Substitute.For<IExchangeAccountRepository>();
        _accounts.GetAsync(_account.Id, Arg.Any<CancellationToken>()).Returns(_account);
        _accounts.GetSecretAsync(_account.Id, Arg.Any<CancellationToken>()).Returns(encrypted);
        _protector = Substitute.For<ISecretProtector>();
        _protector.Decrypt(_account.Id, encrypted).Returns(Token);
        _inspector = Substitute.For<IExchangeKeyInspector>();
        _broker = Substitute.For<IBrokerAccountReader>();
        _broker.ListAccountsAsync(Token, Arg.Any<CancellationToken>()).Returns([Account(BrokerAccountStatus.Open, BrokerAccountAccess.Full)]);
        _ledger = Substitute.For<IExchangeLedger>();
        _ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), "RUB", Arg.Any<CancellationToken>()).Returns(50_000m);
        _catalog = Substitute.For<IInstrumentCatalog>();
        _catalog.ListAsync(MarketCategory.Moex, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Moex, "SBER", "SBER", "RUB")]);
        _live = Substitute.For<ILiveBotRepository>();
        _live.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, LiveBot>());
        _live.LockAsync(_bot.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(LiveBot.New(_bot.Id));
        _executor = Substitute.For<ITransactionExecutor>();
        _executor.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<int>>>(0)(CancellationToken.None));
    }

    // Scenario "Торговля на MOEX выключена": no call to T-Invest, and the bot stays stopped.
    [Test]
    public async Task Start_of_a_bot_on_moex_while_the_trading_on_moex_is_off_is_a_conflict_without_asking_any_exchange()
    {
        ConflictException exception = await Should.ThrowAsync<ConflictException>(() => Handler(enabled: false).Handle(new StartBotCommand(_bot.Id), CancellationToken.None));

        exception.Message.ShouldBe("Trading on MOEX is disabled.");
        _inspector.ReceivedCalls().ShouldBeEmpty();
        _broker.ReceivedCalls().ShouldBeEmpty();
        _ledger.ReceivedCalls().ShouldBeEmpty();
        _live.ReceivedCalls().ShouldBeEmpty();
        await _accounts.DidNotReceiveWithAnyArgs().GetSecretAsync(default, default);
    }

    // Scenarios "Торговля на MOEX включена" and "Бот на MOEX": the account is checked at T-Invest, never as a key of Bybit.
    [Test]
    public async Task Bot_on_moex_starts_while_the_trading_on_moex_is_on()
    {
        await Handler(enabled: true).Handle(new StartBotCommand(_bot.Id), CancellationToken.None);

        await _live.Received(1).SaveAsync(Arg.Is<LiveBot>(bot => bot.Status == LiveBotStatus.Running), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _ledger.Received(1).GetFreeAsync(new ExchangeKey(AccountNumber, Token, Exchange.TInvest), "RUB", Arg.Any<CancellationToken>());
        _inspector.ReceivedCalls().ShouldBeEmpty();
        await _ledger.DidNotReceiveWithAnyArgs().IsHedgeModeAsync(default!, default!, default);
    }

    // Scenario "Счёт T-Invest без полного доступа".
    [Test]
    public async Task Account_the_token_opens_read_only_does_not_start()
    {
        _broker.ListAccountsAsync(Token, Arg.Any<CancellationToken>()).Returns([Account(BrokerAccountStatus.Open, BrokerAccountAccess.ReadOnly)]);

        await ShouldBeRefusedAsync("The token has read-only access to the account.");
    }

    [Test]
    public async Task Account_that_is_no_longer_open_does_not_start()
    {
        _broker.ListAccountsAsync(Token, Arg.Any<CancellationToken>()).Returns([Account(BrokerAccountStatus.Closed, BrokerAccountAccess.Full)]);

        await ShouldBeRefusedAsync("The account is not open.");
    }

    [Test]
    public async Task Account_the_token_no_longer_opens_does_not_start()
    {
        _broker.ListAccountsAsync(Token, Arg.Any<CancellationToken>()).Returns([new BrokerAccount("2000000002", "Другой", BrokerAccountType.Brokerage, BrokerAccountStatus.Open, BrokerAccountAccess.Full)]);

        await ShouldBeRefusedAsync($"The token no longer opens the account {AccountNumber}.");
    }

    [Test]
    public async Task Token_t_invest_no_longer_accepts_does_not_start()
    {
        _broker.ListAccountsAsync(Token, Arg.Any<CancellationToken>()).ThrowsAsync(new ExchangeKeyRejectedException("T-Invest does not accept the token."));

        await ShouldBeRefusedAsync("T-Invest does not accept the token.");
    }

    // Scenario "Рубли на счёте T-Invest": the ledger counts the free roubles (50 000 less the 10 000 blocked), the start counts the bot of 45 000.
    [Test]
    public async Task Bot_on_moex_does_not_start_without_enough_free_roubles()
    {
        _ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), "RUB", Arg.Any<CancellationToken>()).Returns(40_000m);

        ConflictException exception = await Should.ThrowAsync<ConflictException>(() => Handler(enabled: true).Handle(new StartBotCommand(_bot.Id), CancellationToken.None));

        exception.Message.ShouldStartWith("Not enough free money on the account: 40000 RUB");
    }

    private async Task ShouldBeRefusedAsync(string reason)
    {
        ConflictException exception = await Should.ThrowAsync<ConflictException>(() => Handler(enabled: true).Handle(new StartBotCommand(_bot.Id), CancellationToken.None));

        exception.Message.ShouldBe(reason);
        await _live.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default);
        _inspector.ReceivedCalls().ShouldBeEmpty();
    }

    private static BrokerAccount Account(BrokerAccountStatus status, BrokerAccountAccess access)
    {
        return new BrokerAccount(AccountNumber, "Брокерский счёт", BrokerAccountType.Brokerage, status, access);
    }

    private StartBotCommandHandler Handler(bool enabled)
    {
        return new StartBotCommandHandler(
            _library,
            _accounts,
            _protector,
            _inspector,
            _broker,
            _ledger,
            _catalog,
            _live,
            Substitute.For<IIdeaTradeRepository>(),
            _executor,
            new MoexTradingSettings(enabled),
            new FakeTimeProvider(Now));
    }
}