using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.IdeaTrades.Commands.CreateIdeaTrade;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaTrades;

/// <summary>Requirement "Плечо перед входом на linear": the manual entry sets the leverage of the settings on an empty symbol before the trade is saved.</summary>
public class CreateIdeaTradeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 13, 41, 10, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Instrument Dkng = new Instrument(MarketCategory.Linear, "DKNGUSDT");
    private static readonly Instrument DkngSpot = new Instrument(MarketCategory.Spot, "DKNGUSDT");
    private static readonly ExchangeKey Key = new ExchangeKey("key", "secret");

    private IIdeaRepository _ideas = null!;
    private IIdeaTradeRepository _trades = null!;
    private IIdeaTradingSettingsRepository _settings = null!;
    private IExchangeLedger _ledger = null!;
    private IOrderGateway _gateway = null!;
    private ILiveTradingJournal _journal = null!;
    private IMarketHistoryClient _history = null!;
    private ILiveBotRepository _live = null!;
    private ITransactionExecutor _executor = null!;
    private CreateIdeaTradeCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _ideas = Substitute.For<IIdeaRepository>();
        _trades = Substitute.For<IIdeaTradeRepository>();
        _settings = Substitute.For<IIdeaTradingSettingsRepository>();
        _ledger = Substitute.For<IExchangeLedger>();
        _gateway = Substitute.For<IOrderGateway>();
        _journal = Substitute.For<ILiveTradingJournal>();
        _history = Substitute.For<IMarketHistoryClient>();
        _live = Substitute.For<ILiveBotRepository>();
        _executor = Substitute.For<ITransactionExecutor>();
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _live.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, LiveBot>());
        _history.GetLastPriceAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>()).Returns(100m);
        _ledger.GetFreeAsync(Key, "USDT", Arg.Any<CancellationToken>()).Returns(1000m);
        _ledger.GetLeverageAsync(Key, Dkng, Arg.Any<CancellationToken>()).Returns(10m);
        _ledger.GetOpenOrdersAsync(Key, Dkng, Arg.Any<CancellationToken>()).Returns([]);
        _gateway.SetLeverageAsync(Key, Dkng, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new OrderOutcome(OrderOutcomeKind.Accepted));
        Settings(leverage: 1);
        _executor.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<Guid>>>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<Func<CancellationToken, Task<Guid>>>(0)(CancellationToken.None));
        _handler = new CreateIdeaTradeCommandHandler(Calculator(), _trades, _live, _executor, _gateway, _journal, new FakeTimeProvider(Now));
    }

    // Scenario "Символ с плечом 10×": the leverage is set to the one of the settings, then the trade is saved with it.
    [Test]
    public async Task Leverage_of_an_empty_symbol_is_set_before_the_trade_is_saved()
    {
        Idea idea = Idea(Dkng);

        await _handler.Handle(new CreateIdeaTradeCommand(idea.Id, AccountId, 50m), CancellationToken.None);

        Received.InOrder(() =>
        {
            _gateway.SetLeverageAsync(Key, Dkng, 1, Arg.Any<CancellationToken>());
            _trades.CreateAsync(Arg.Is<IdeaTrade>(trade => trade!.Leverage == 1), Arg.Any<IdeaTradeOrder>(), Now, Arg.Any<CancellationToken>());
        });
    }

    // A trade on linear keeps the leverage of the settings it entered with.
    [Test]
    public async Task Trade_keeps_the_leverage_of_the_settings()
    {
        Settings(leverage: 3);
        Idea idea = Idea(Dkng);

        await _handler.Handle(new CreateIdeaTradeCommand(idea.Id, AccountId, 50m), CancellationToken.None);

        await _gateway.Received(1).SetLeverageAsync(Key, Dkng, 3, Arg.Any<CancellationToken>());
        await _trades.Received(1).CreateAsync(Arg.Is<IdeaTrade>(trade => trade!.Leverage == 3), Arg.Any<IdeaTradeOrder>(), Now, Arg.Any<CancellationToken>());
    }

    // Scenario "Плечо уже нужное".
    [Test]
    public async Task Leverage_the_symbol_has_already_is_not_set()
    {
        _ledger.GetLeverageAsync(Key, Dkng, Arg.Any<CancellationToken>()).Returns(1m);
        Idea idea = Idea(Dkng);

        await _handler.Handle(new CreateIdeaTradeCommand(idea.Id, AccountId, 50m), CancellationToken.None);

        await _gateway.DidNotReceiveWithAnyArgs().SetLeverageAsync(default!, default!, default, default);
        await _trades.Received(1).CreateAsync(Arg.Any<IdeaTrade>(), Arg.Any<IdeaTradeOrder>(), Now, Arg.Any<CancellationToken>());
    }

    // Scenario "Bybit отказал": above the largest leverage of the symbol; the message of Bybit goes to the log.
    [Test]
    public async Task Leverage_bybit_refuses_refuses_the_entry_and_saves_no_trade()
    {
        Settings(leverage: 50);
        _gateway.SetLeverageAsync(Key, Dkng, 50, Arg.Any<CancellationToken>()).Returns(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "leverage invalid"));
        Idea idea = Idea(Dkng);

        IdeaTradeRefusedException refused = await Should.ThrowAsync<IdeaTradeRefusedException>(() => _handler.Handle(new CreateIdeaTradeCommand(idea.Id, AccountId, 50m), CancellationToken.None));

        refused.Reasons.ShouldBe([IdeaTradeRefusal.LeverageNotSet]);
        await _trades.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
        _journal.Received(1).LeverageRefused(AccountId, Dkng, 50, "leverage invalid");
    }

    [Test]
    public async Task Leverage_without_an_answer_of_bybit_is_an_exchange_that_did_not_answer()
    {
        _gateway.SetLeverageAsync(Key, Dkng, 1, Arg.Any<CancellationToken>()).Returns(new OrderOutcome(OrderOutcomeKind.Transient, Reason: "Too many visits"));
        Idea idea = Idea(Dkng);

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _handler.Handle(new CreateIdeaTradeCommand(idea.Id, AccountId, 50m), CancellationToken.None));

        await _trades.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
    }

    // Scenario "Вход на споте": the spot has no leverage to set, and the trade keeps one.
    [Test]
    public async Task Spot_sets_no_leverage_and_its_trade_keeps_the_leverage_of_one()
    {
        Settings(leverage: 5);
        Idea idea = Idea(DkngSpot);

        await _handler.Handle(new CreateIdeaTradeCommand(idea.Id, AccountId, 50m), CancellationToken.None);

        await _gateway.DidNotReceiveWithAnyArgs().SetLeverageAsync(default!, default!, default, default);
        await _trades.Received(1).CreateAsync(Arg.Is<IdeaTrade>(trade => trade!.Leverage == 1), Arg.Any<IdeaTradeOrder>(), Now, Arg.Any<CancellationToken>());
    }

    private void Settings(int leverage)
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(IdeaTradingSettings.Default with { AccountId = AccountId, MaxAmount = 500m, MaxTotalRisk = 100m, MinRiskReward = 0m, Leverage = leverage });
    }

    // A long at 100 with a stop 0.2 % away: no leverage up to 250× puts it too far.
    private Idea Idea(Instrument instrument)
    {
        Idea generated = IdeaGenerator.Generate(Now.AddMinutes(-1), instrument: instrument);
        Idea idea = generated with { Track = generated.Track with { Plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 99.8m, 101m) } };
        _ideas.GetAsync(idea.Id, Arg.Any<CancellationToken>()).Returns(idea);

        return idea;
    }

    private IdeaTradeEntryCalculator Calculator()
    {
        IExchangeAccountRepository accounts = Substitute.For<IExchangeAccountRepository>();
        var account = new ExchangeAccount(AccountId, "Bybit · ideas", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear, MarketCategory.Spot }, Now);
        accounts.GetAsync(AccountId, Arg.Any<CancellationToken>()).Returns(account);
        accounts.GetSecretAsync(AccountId, Arg.Any<CancellationToken>()).Returns(new EncryptedSecret([1], [2], [3]));
        ISecretProtector protector = Substitute.For<ISecretProtector>();
        protector.Decrypt(AccountId, Arg.Any<EncryptedSecret>()).Returns("secret");
        IExchangeKeyInspector inspector = Substitute.For<IExchangeKeyInspector>();
        inspector.InspectAsync("key", "secret", Arg.Any<CancellationToken>()).Returns(new ExchangeKeyInfo(100, false, true, false, new HashSet<MarketCategory> { MarketCategory.Linear, MarketCategory.Spot }));
        IInstrumentRulesProvider rules = Substitute.For<IInstrumentRulesProvider>();
        rules.GetRulesAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>()).Returns(new InstrumentRules(0.01m, 0.01m, 0.01m, 5m));
        IInstrumentCatalog catalog = Substitute.For<IInstrumentCatalog>();
        catalog.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Linear, "DKNGUSDT", "DKNG", "USDT", true, true)]);
        catalog.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Spot, "DKNGUSDT", "DKNG", "USDT", true)]);
        IIdeaBucketRepository buckets = Substitute.For<IIdeaBucketRepository>();
        buckets.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        IStrategyLibraryRepository library = Substitute.For<IStrategyLibraryRepository>();
        library.GetBotsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var fees = new BacktestSettings
        {
            DefaultFees = new Dictionary<string, FeeRateSettings>
            {
                ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m },
                ["spot"] = new FeeRateSettings { Limit = 0.1m, Market = 0.1m }
            }
        };

        return new IdeaTradeEntryCalculator(_ideas, _settings, _trades, accounts, protector, inspector, _ledger, _history, rules, catalog, _live, library, buckets, new IdeasEngineVersion(IdeasEngineVersion.Current), fees);
    }
}