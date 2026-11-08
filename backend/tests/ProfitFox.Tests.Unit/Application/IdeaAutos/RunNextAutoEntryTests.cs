using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaAutos.Commands.RunNextAutoEntry;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
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
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaAutos;

/// <summary>Requirements "Вход автомата с деньгами", "Вход по настройкам на минуту идеи", "Опоздавший вход" and "Дневной стоп останавливает автоматы с деньгами".</summary>
public class RunNextAutoEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 13, 41, 10, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Instrument Wif = IdeaGenerator.Wif;

    private IIdeaAutoEntryRepository _entries = null!;
    private IIdeaAutoRepository _autos = null!;
    private IIdeaAutoRefusalRepository _refusals = null!;
    private IIdeaRepository _ideas = null!;
    private IPaperIdeaTradeRepository _paper = null!;
    private IIdeaTradeRepository _trades = null!;
    private IIdeaTradingSettingsRepository _settings = null!;
    private IExchangeLedger _ledger = null!;
    private IMarketHistoryClient _history = null!;
    private ILiveBotRepository _live = null!;
    private ITransactionExecutor _executor = null!;
    private IAsyncDisposable _lock = null!;

    private IExchangeAccountRepository _accounts = null!;
    private IInstrumentCatalog _catalog = null!;
    private IdeaAuto _auto = null!;
    private Idea _idea = null!;
    private IdeaAutoEntry _entry = null!;
    private RunNextAutoEntryCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _entries = Substitute.For<IIdeaAutoEntryRepository>();
        _autos = Substitute.For<IIdeaAutoRepository>();
        _refusals = Substitute.For<IIdeaAutoRefusalRepository>();
        _ideas = Substitute.For<IIdeaRepository>();
        _paper = Substitute.For<IPaperIdeaTradeRepository>();
        _trades = Substitute.For<IIdeaTradeRepository>();
        _settings = Substitute.For<IIdeaTradingSettingsRepository>();
        _ledger = Substitute.For<IExchangeLedger>();
        _history = Substitute.For<IMarketHistoryClient>();
        _live = Substitute.For<ILiveBotRepository>();
        _executor = Substitute.For<ITransactionExecutor>();
        _lock = Substitute.For<IAsyncDisposable>();
        _auto = IdeaAutoGenerator.Generate(AccountId, "Импульс лонг", mode: IdeaAutoMode.Live, amount: 300m);
        Idea generated = IdeaGenerator.Generate(Now.AddMinutes(-1));
        _idea = generated with { Track = generated.Track with { Plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m) } };
        _entry = new IdeaAutoEntry(Guid.NewGuid(), _idea.Id, _auto.Id, AccountId, 300m, 1.2m, 0, 0, Now.AddMinutes(-1), Now.AddSeconds(-20));
        _entries.TryLockEntriesAsync(Arg.Any<CancellationToken>()).Returns(_lock);
        _entries.GetNextAsync(Arg.Any<CancellationToken>()).Returns(_entry);
        _entries.LockAsync(_entry.Id, Arg.Any<CancellationToken>()).Returns(_entry);
        _autos.GetAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto);
        _autos.LockAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto);
        _autos.GetEveryAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IdeaAuto> { [_auto.Id] = _auto });
        _ideas.GetAsync(_idea.Id, Arg.Any<CancellationToken>()).Returns(_idea);
        _paper.GetOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<PaperIdeaTrade>());
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _trades.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(IdeaTradingSettings.Default with { AccountId = AccountId, MaxAmount = 500m, MaxOpenTrades = 8, MaxTotalRisk = 100m });
        _history.GetLastPriceAsync(Wif, Arg.Any<CancellationToken>()).Returns(100m);
        _ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1000m);
        _live.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, LiveBot>());
        _executor.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<Func<CancellationToken, Task<int>>>(0)(CancellationToken.None));
        _handler = new RunNextAutoEntryCommandHandler(_entries, _autos, _refusals, _ideas, _paper, _trades, Calculator(), _live, _executor, new FakeTimeProvider(Now));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _lock.DisposeAsync();
    }

    // Scenario "Вход по рынку": the trade is the auto's, with the amount and the account of the request, and its entry order is written; the request is gone.
    [Test]
    public async Task Request_that_breaks_nothing_becomes_a_trade_of_the_auto_with_its_entry_order()
    {
        (await Run()).ShouldBeTrue();

        await _live.Received(1).LockAccountAsync(AccountId, Arg.Any<CancellationToken>());
        await _trades.Received(1).LockCreationAsync(Arg.Any<CancellationToken>());
        await _trades.Received(1).CreateAsync(
            Arg.Is<IdeaTrade>(trade => trade!.AutoId == _auto.Id && trade.AccountId == AccountId && trade.IdeaId == _idea.Id && trade.Amount == 300m && trade.Quantity == 3m
                                       && trade.Status == IdeaTradeStatus.AwaitingEntry && trade.LastOrderNumber == 1),
            Arg.Is<IdeaTradeOrder>(order => order!.Number == 1 && order.Role == IdeaTradeOrderRole.Entry),
            Now,
            Arg.Any<CancellationToken>());
        await _entries.Received(1).DeleteAsync(_entry.Id, Arg.Any<CancellationToken>());
        await _refusals.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _lock.Received(1).DisposeAsync();
    }

    // Scenario "Правка между минутой и входом": the request carries 300, the auto has 200 now, and the lowest reward to risk of the request is the one the entry is checked by.
    [Test]
    public async Task Entry_goes_by_the_settings_of_the_request_not_by_those_of_the_auto_now()
    {
        _autos.GetAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto with { Amount = 200m });
        _entries.GetNextAsync(Arg.Any<CancellationToken>()).Returns(_entry with { MinRiskReward = 3m });

        await Run();

        await _trades.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Count == 1 && refusal.Reasons[0] == IdeaTradeRefusal.RiskRewardTooLow && refusal.Detail!.Limit == 3m), Arg.Any<CancellationToken>());
    }

    // Scenario "Не хватает денег": «свободно 212.40 из 300 USDT».
    [Test]
    public async Task Refusal_is_written_with_the_reasons_of_the_entry_and_its_detail_and_the_request_is_gone()
    {
        _ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(212.40m);

        (await Run()).ShouldBeTrue();

        await _refusals.Received(1).AddAsync(
            Arg.Is<IdeaAutoRefusal>(refusal => refusal!.IdeaId == _idea.Id && refusal.AutoId == _auto.Id && refusal.Mode == IdeaAutoMode.Live && refusal.At == Now
                                               && refusal.Instrument == Wif && refusal.Setup == IdeaSetup.Impulse && refusal.Reasons.Single() == IdeaTradeRefusal.NotEnoughMoney
                                               && refusal.Detail!.Actual == 212.40m && refusal.Detail.Limit == 300m),
            Arg.Any<CancellationToken>());
        await _entries.Received(1).DeleteAsync(_entry.Id, Arg.Any<CancellationToken>());
        await _trades.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
    }

    // Scenario "Сначала верхний не смог" is two calls: the request of the lower auto is the next one once the upper one is gone.
    [Test]
    public async Task Next_call_takes_the_next_request_once_the_first_is_gone()
    {
        _ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(212.40m);
        await Run();
        _ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1000m);

        await Run();

        await _trades.Received(1).CreateAsync(Arg.Any<IdeaTrade>(), Arg.Any<IdeaTradeOrder>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // Scenario "Дневной стоп": −30.15 of 30 holds the autos with money.
    [Test]
    public async Task Daily_stop_that_holds_refuses_the_request()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(IdeaTradingSettings.Default with { AccountId = AccountId, MaxAmount = 500m, MaxOpenTrades = 8, MaxTotalRisk = 100m, DailyLossLimit = 30m });
        _trades.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Closed(-30.15m, Now.AddHours(-1))]);

        await Run();

        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.DailyStop && refusal.Detail!.Actual == -30.15m && refusal.Detail.Limit == 30m), Arg.Any<CancellationToken>());
    }

    // Scenario "Worker не запущен": the request is older than 60 seconds, nothing is asked of the exchange.
    [Test]
    public async Task Request_older_than_a_minute_is_refused_as_late_without_asking_the_exchange()
    {
        _entries.GetNextAsync(Arg.Any<CancellationToken>()).Returns(_entry with { CreatedAt = Now.AddSeconds(-61) });

        (await Run()).ShouldBeTrue();

        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.Late && refusal.Detail == null), Arg.Any<CancellationToken>());
        await _entries.Received(1).DeleteAsync(_entry.Id, Arg.Any<CancellationToken>());
        await _history.DidNotReceiveWithAnyArgs().GetLastPriceAsync(default!, default);
    }

    [Test]
    public async Task Request_of_exactly_a_minute_is_still_on_time()
    {
        _entries.GetNextAsync(Arg.Any<CancellationToken>()).Returns(_entry with { CreatedAt = Now.AddSeconds(-60) });

        await Run();

        await _trades.Received(1).CreateAsync(Arg.Any<IdeaTrade>(), Arg.Any<IdeaTradeOrder>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // Scenario "Выключили до входа" and the deleted auto.
    [TestCase(IdeaAutoMode.Off)]
    [TestCase(IdeaAutoMode.Paper)]
    public async Task Auto_that_is_not_with_money_any_more_is_refused_as_off(IdeaAutoMode mode)
    {
        _autos.GetAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto with { Mode = mode });

        await Run();

        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.AutoOff), Arg.Any<CancellationToken>());
        await _history.DidNotReceiveWithAnyArgs().GetLastPriceAsync(default!, default);
    }

    [Test]
    public async Task Auto_that_was_deleted_is_refused_as_off()
    {
        _autos.GetAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto with { DeletedAt = Now.AddSeconds(-5) });

        await Run();

        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.AutoOff), Arg.Any<CancellationToken>());
    }

    // Review P2: the owner switches the auto off or deletes it while the exchange is asked; the lock of the auto in the unit of work sees it.
    [Test]
    public async Task Auto_switched_off_or_deleted_while_the_exchange_was_asked_is_refused_as_off()
    {
        _autos.LockAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto with { Mode = IdeaAutoMode.Off }, _auto with { DeletedAt = Now });

        await Run();
        await Run();

        await _trades.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
        await _refusals.Received(2).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.AutoOff), Arg.Any<CancellationToken>());
        await _entries.Received(2).DeleteAsync(_entry.Id, Arg.Any<CancellationToken>());
    }

    // Review P2: the pair is held by account: the request enters on its own account, whatever the account of the auto is by now.
    [Test]
    public async Task Pair_is_held_by_a_trade_on_the_account_of_the_request_not_of_the_auto_now()
    {
        var other = Guid.NewGuid();
        _autos.GetAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto with { AccountId = other });
        _autos.LockAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto with { AccountId = other });
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Manual(AccountId)]);

        await Run();

        await _trades.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.PairTaken), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Trade_on_the_account_the_auto_has_now_does_not_hold_the_pair_of_the_request()
    {
        var other = Guid.NewGuid();
        _autos.GetAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto with { AccountId = other });
        _autos.LockAsync(_auto.Id, Arg.Any<CancellationToken>()).Returns(_auto with { AccountId = other });
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Manual(other)]);

        await Run();

        await _trades.Received(1).CreateAsync(Arg.Is<IdeaTrade>(trade => trade!.AccountId == AccountId), Arg.Any<IdeaTradeOrder>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // Review: a request whose entry cannot be prepared for another reason than the exchange is refused, not tried again until it is late, so the
    // requests behind it go on. The account of the request was deleted: there is no key to enter with.
    [Test]
    public async Task Request_whose_account_is_gone_is_refused_for_the_key_and_the_queue_goes_on()
    {
        _accounts.GetAsync(AccountId, Arg.Any<CancellationToken>()).Returns((ExchangeAccount?)null);

        (await Run()).ShouldBeTrue();

        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.NoKey), Arg.Any<CancellationToken>());
        await _entries.Received(1).DeleteAsync(_entry.Id, Arg.Any<CancellationToken>());
        await _trades.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
    }

    // After the review: an account of T-Invest has no key of Bybit; the entry is refused for the key before any secret is read or Bybit is asked.
    [Test]
    public async Task Request_with_a_tinvest_account_is_refused_for_the_key_without_reading_the_secret()
    {
        _accounts.GetAsync(AccountId, Arg.Any<CancellationToken>())
            .Returns(new ExchangeAccount(AccountId, "Брокерский", Exchange.TInvest, null, null, "2000000001", new HashSet<MarketCategory> { MarketCategory.Moex }, Now));

        (await Run()).ShouldBeTrue();

        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.NoKey), Arg.Any<CancellationToken>());
        await _accounts.DidNotReceiveWithAnyArgs().GetSecretAsync(default, default);
    }

    // The exchange no longer lists the pair of the idea, so the preparation fails: the request is refused with the error of the entry.
    [Test]
    public async Task Request_whose_entry_fails_to_be_prepared_is_refused_with_the_error_and_the_queue_goes_on()
    {
        _catalog.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns([]);

        (await Run()).ShouldBeTrue();

        await _refusals.Received(1).AddAsync(Arg.Is<IdeaAutoRefusal>(refusal => refusal!.Reasons.Single() == IdeaTradeRefusal.EntryError), Arg.Any<CancellationToken>());
        await _entries.Received(1).DeleteAsync(_entry.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task No_request_is_no_work()
    {
        _entries.GetNextAsync(Arg.Any<CancellationToken>()).Returns((IdeaAutoEntry?)null);

        (await Run()).ShouldBeFalse();
    }

    // Decision 6: another Worker holds the lock and works the requests.
    [Test]
    public async Task Request_is_left_to_the_Worker_that_holds_the_lock()
    {
        _entries.TryLockEntriesAsync(Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);

        (await Run()).ShouldBeFalse();

        await _entries.DidNotReceiveWithAnyArgs().GetNextAsync(default);
    }

    // Decision 6: the exchange did not answer: the request stays for the next round, or for the end of its minute.
    [Test]
    public async Task Request_stays_when_the_exchange_does_not_answer()
    {
        _history.GetLastPriceAsync(Wif, Arg.Any<CancellationToken>()).Returns(Task.FromException<decimal>(new ExchangeUnavailableException("Bybit is unavailable.")));

        (await Run()).ShouldBeFalse();

        await _entries.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Test]
    public async Task Request_that_is_gone_under_the_lock_is_left_alone()
    {
        _entries.LockAsync(_entry.Id, Arg.Any<CancellationToken>()).Returns((IdeaAutoEntry?)null);

        await Run();

        await _trades.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
        await _entries.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Test]
    public async Task Late_request_that_is_gone_under_the_lock_is_left_alone()
    {
        _entries.GetNextAsync(Arg.Any<CancellationToken>()).Returns(_entry with { CreatedAt = Now.AddSeconds(-90) });
        _entries.LockAsync(_entry.Id, Arg.Any<CancellationToken>()).Returns((IdeaAutoEntry?)null);

        await Run();

        await _refusals.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Test]
    public async Task Request_of_an_idea_that_is_gone_is_dropped_without_a_refusal()
    {
        _ideas.GetAsync(_idea.Id, Arg.Any<CancellationToken>()).Returns((Idea?)null);

        (await Run()).ShouldBeTrue();

        await _entries.Received(1).DeleteAsync(_entry.Id, Arg.Any<CancellationToken>());
        await _refusals.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    private Task<bool> Run()
    {
        return _handler.Handle(new RunNextAutoEntryCommand(), CancellationToken.None);
    }

    // An open manual trade of the pair of the idea on the account.
    private IdeaTrade Manual(Guid account)
    {
        return IdeaTrade.Create(Guid.NewGuid(), _idea, account, 100m, new IdeaTradeSize(1m, 100m, 1m, 2m, 2m), Now.AddHours(-1)).EntryGrown(new LegTotals(1m, 100m, 0m), Now.AddHours(-1));
    }

    private static IdeaTrade Closed(decimal result, DateTimeOffset at)
    {
        IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(at.AddHours(-1)), AccountId, 100m, new IdeaTradeSize(1m, 100m, 1m, 2m, 2m), at.AddHours(-1))
            .EntryGrown(new LegTotals(1m, 100m, 0m), at.AddHours(-1));

        // The entry cost 100 and the exit brings it back less the loss.
        return trade.ExitGrown(IdeaTradeLeg.StopLoss, new LegTotals(1m, 100m + result, 0m), at);
    }

    // The calculator over the world of the test: an account with a key that trades linear, 1000 free, the rules of a lot.
    private IdeaTradeEntryCalculator Calculator()
    {
        IExchangeAccountRepository accounts = _accounts = Substitute.For<IExchangeAccountRepository>();
        var account = new ExchangeAccount(AccountId, "Bybit · ideas", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Now);
        accounts.GetAsync(AccountId, Arg.Any<CancellationToken>()).Returns(account);
        accounts.GetSecretAsync(AccountId, Arg.Any<CancellationToken>()).Returns(new EncryptedSecret([1], [2], [3]));
        ISecretProtector protector = Substitute.For<ISecretProtector>();
        protector.Decrypt(AccountId, Arg.Any<EncryptedSecret>()).Returns("secret");
        IExchangeKeyInspector inspector = Substitute.For<IExchangeKeyInspector>();
        inspector.InspectAsync("key", "secret", Arg.Any<CancellationToken>()).Returns(new ExchangeKeyInfo(100, false, true, false, new HashSet<MarketCategory> { MarketCategory.Linear }));
        IInstrumentRulesProvider rules = Substitute.For<IInstrumentRulesProvider>();
        rules.GetRulesAsync(Wif, Arg.Any<CancellationToken>()).Returns(new InstrumentRules(0.01m, 1m, 1m, 5m));
        IInstrumentCatalog catalog = _catalog = Substitute.For<IInstrumentCatalog>();
        catalog.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Linear, "WIFUSDT", "WIF", "USDT", true, true)]);
        IIdeaBucketRepository buckets = Substitute.For<IIdeaBucketRepository>();
        buckets.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        IStrategyLibraryRepository library = Substitute.For<IStrategyLibraryRepository>();
        library.GetBotsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var fees = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };

        return new IdeaTradeEntryCalculator(_ideas, _settings, _trades, accounts, protector, inspector, _ledger, _history, rules, catalog, _live, library, buckets, new IdeasEngineVersion(IdeasEngineVersion.Current), fees);
    }
}