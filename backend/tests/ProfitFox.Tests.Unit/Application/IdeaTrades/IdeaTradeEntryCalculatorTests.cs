using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.ExchangeAccounts;
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
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaTrades;

/// <summary>The entry of an idea for an auto: the lowest reward to risk of the auto replaces the one of the settings, and the checks of the auto join the reasons.</summary>
public class IdeaTradeEntryCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 13, 20, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.01m, 0.01m, 5m);

    private static readonly ExchangeKey Key = new ExchangeKey("key", "secret");

    private IIdeaTradeRepository _trades = null!;
    private IExchangeLedger _ledger = null!;
    private IInstrumentCatalog _catalog = null!;
    private ILiveBotRepository _live = null!;
    private IStrategyLibraryRepository _library = null!;
    private IdeaTradeEntryCalculator _calculator = null!;

    // NUnit keeps one instance for all the tests of a class: the fakes are made anew for each.
    [SetUp]
    public void SetUp()
    {
        _trades = Substitute.For<IIdeaTradeRepository>();
        _ledger = Substitute.For<IExchangeLedger>();
        _catalog = Substitute.For<IInstrumentCatalog>();
        _live = Substitute.For<ILiveBotRepository>();
        _library = Substitute.For<IStrategyLibraryRepository>();
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _live.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, LiveBot>());
        _library.GetBotsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _catalog.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Linear, "ETHUSDT", "ETH", "USDT")]);
        _catalog.ListAsync(MarketCategory.Spot, Arg.Any<CancellationToken>()).Returns([new ListedInstrument(MarketCategory.Spot, "ETHUSDT", "ETH", "USDT")]);
        _ledger.GetFreeAsync(Key, "USDT", Arg.Any<CancellationToken>()).Returns(10_000m);
        _ledger.GetLinearPositionAsync(Arg.Any<ExchangeKey>(), Arg.Any<Instrument>(), Arg.Any<CancellationToken>()).Returns(new LinearPosition(1m, 0m));
        _ledger.GetOpenOrdersAsync(Key, Arg.Any<Instrument>(), Arg.Any<CancellationToken>()).Returns([]);
        _calculator = new IdeaTradeEntryCalculator(
            Substitute.For<IIdeaRepository>(),
            Substitute.For<IIdeaTradingSettingsRepository>(),
            _trades,
            Substitute.For<IExchangeAccountRepository>(),
            Substitute.For<ISecretProtector>(),
            Substitute.For<IExchangeKeyInspector>(),
            _ledger,
            Substitute.For<IMarketHistoryClient>(),
            Substitute.For<IInstrumentRulesProvider>(),
            _catalog,
            _live,
            _library,
            Substitute.For<IIdeaBucketRepository>(),
            new IdeasEngineVersion(IdeasEngineVersion.Current),
            new BacktestSettings());
    }

    // Requirement "Обещание на входе", scenario "Издержки от цены входа" (decision 9 of add-own-idea-numbers): the bucket without costs −0.02 R
    // with −0.06…+0.02 R, the stop of the idea 98, its entry 100, and the trade enters by the market at 99: the promise is −0.02 − 0.0021 × 99 / 1
    // = −0.2279 R with ▼ by −0.2679…−0.1879 R, not −0.125 R by the entry of the idea nor the −0.21 R of the bucket; N stays that of the bucket.
    [Test]
    public async Task Promise_is_the_own_numbers_of_the_idea_from_the_price_of_the_entry()
    {
        EntryPreparation preparation = Preparation(minRiskReward: 1.2m);
        var bucket = new BucketStatistics(400, 180, 200, 20, 0.45m, 0.4m, 0.5m, -0.21m, -0.25m, -0.17m) { GrossExpectancy = -0.02m, GrossExpectancyLow = -0.06m, GrossExpectancyHigh = 0.02m };
        preparation = preparation with { Price = 99m, Fees = new FeeRates(0.02m, 0.055m, 0.05m), Buckets = [new IdeaBucket(KeyOf(preparation.Idea), 90, bucket)] };

        IdeaPromise promise = (await _calculator.EvaluateAsync(preparation, null, CancellationToken.None)).Promise;

        (promise.Badge, promise.Probability, promise.Expectancy, promise.Cases).ShouldBe((IdeaBadge.Down, 0.45m, -0.2279m, 400));
    }

    // Scenario "Мало данных": no badge and no probability; the cases and the expectancy of the idea, +0.40 − 0.0021 × 99 / 1 = +0.1921 R.
    [Test]
    public async Task Promise_of_a_bucket_of_too_little_data_has_the_cases_and_the_expectancy_only()
    {
        EntryPreparation preparation = Preparation(minRiskReward: 1.2m);
        var bucket = new BucketStatistics(12, 6, 6, 0, null, null, null, 0.3m, null, null) { GrossExpectancy = 0.4m };
        preparation = preparation with { Price = 99m, Fees = new FeeRates(0.02m, 0.055m, 0.05m), Buckets = [new IdeaBucket(KeyOf(preparation.Idea), 90, bucket)] };

        IdeaPromise promise = (await _calculator.EvaluateAsync(preparation, null, CancellationToken.None)).Promise;

        (promise.Badge, promise.Probability, promise.Expectancy, promise.Cases).ShouldBe((null, null, 0.1921m, 12));
    }

    // Scenario "Ниже настроек, выше автомата": the reward to risk of the entry is about 1.32, the settings want 1.5 and the auto 1.2.
    [Test]
    public async Task Auto_enters_with_its_own_lowest_reward_to_risk_below_that_of_the_settings()
    {
        EntryCalculation manual = await _calculator.EvaluateAsync(Preparation(), null, CancellationToken.None);
        EntryCalculation auto = await _calculator.EvaluateAsync(Preparation(), Entry(minRiskReward: 1.2m), CancellationToken.None);

        manual.Reasons.ShouldBe([IdeaTradeRefusal.RiskRewardTooLow]);
        auto.Reasons.ShouldBeEmpty();
    }

    // Scenario "R:R ниже минимума": the numbers of the reason are the entry's and the minimum of the auto.
    [Test]
    public async Task Auto_that_wants_more_is_refused_with_both_numbers()
    {
        EntryCalculation auto = await _calculator.EvaluateAsync(Preparation(), Entry(minRiskReward: 1.4m), CancellationToken.None);

        auto.Reasons.ShouldBe([IdeaTradeRefusal.RiskRewardTooLow]);
        auto.Detail.ShouldBe(RefusalDetail.Numbers(auto.Size.RiskReward, 1.4m));
        Math.Round(auto.Size.RiskReward, 2).ShouldBe(1.32m);
    }

    [Test]
    public async Task Manual_entry_has_no_detail_when_nothing_stops_it()
    {
        EntryCalculation manual = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m), null, CancellationToken.None);

        manual.Reasons.ShouldBeEmpty();
        manual.Detail.ShouldBeNull();
    }

    [Test]
    public async Task Pair_held_by_a_manual_trade_of_the_account_is_named_for_an_auto_only()
    {
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Held()]);

        EntryCalculation manual = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m), null, CancellationToken.None);
        EntryCalculation auto = await _calculator.EvaluateAsync(Preparation(), Entry(minRiskReward: 1.2m), CancellationToken.None);

        manual.Reasons.ShouldBeEmpty();
        auto.Reasons.ShouldBe([IdeaTradeRefusal.PairTaken]);
        auto.Detail!.Holder!.Kind.ShouldBe(PairHolderKind.Manual);
    }

    [Test]
    public async Task Daily_stop_and_the_open_trades_of_a_side_are_named_for_an_auto()
    {
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Held(Instrument("A")), Held(Instrument("B"))]);
        AutoEntry entry = Entry(minRiskReward: 1.2m) with { DailyLoss = new DailyLossState(-31m, 30m, Now, Now.AddDays(1)) };

        EntryCalculation auto = await _calculator.EvaluateAsync(Preparation(maxSameDirection: 2), entry, CancellationToken.None);

        auto.Reasons.ShouldBe([IdeaTradeRefusal.DailyStop, IdeaTradeRefusal.TooManySameDirection]);
        auto.Detail.ShouldBe(RefusalDetail.Numbers(-31m, 30m));
    }

    [Test]
    public async Task Reasons_of_the_entry_come_before_those_of_the_auto()
    {
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Held()]);

        EntryCalculation auto = await _calculator.EvaluateAsync(Preparation(), Entry(minRiskReward: 1.4m), CancellationToken.None);

        auto.Reasons.ShouldBe([IdeaTradeRefusal.RiskRewardTooLow, IdeaTradeRefusal.PairTaken]);
    }

    // Decision 1: the entry takes the budget with the amount it asks for (300.5), not the one of its volume rounded down to the step (300).
    [Test]
    public async Task Budget_of_the_auto_is_checked_with_the_amount_the_request_asks_for()
    {
        AutoEntry entry = Entry(minRiskReward: 1.2m, budget: 350.2m);
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Held(Instrument("SOL"), entry.Auto.Id)]);

        EntryCalculation auto = await _calculator.EvaluateAsync(Preparation(amount: 300.5m), entry, CancellationToken.None);

        auto.Size.Amount.ShouldBe(300m);
        auto.Reasons.ShouldBe([IdeaTradeRefusal.BudgetTaken]);
        auto.Detail.ShouldBe(RefusalDetail.Numbers(50m, 350.2m));
    }

    [Test]
    public async Task Budget_of_all_the_autos_of_the_settings_is_named_for_an_auto_only()
    {
        AutoEntry entry = Entry(minRiskReward: 1.2m);
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([Held(Instrument("SOL"), entry.Auto.Id)]);

        EntryCalculation manual = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, autosBudget: 340m), null, CancellationToken.None);
        EntryCalculation auto = await _calculator.EvaluateAsync(Preparation(autosBudget: 340m), entry, CancellationToken.None);

        manual.Reasons.ShouldBeEmpty();
        auto.Reasons.ShouldBe([IdeaTradeRefusal.AutosBudgetTaken]);
        auto.Detail.ShouldBe(RefusalDetail.Numbers(50m, 340m));
    }

    // Scenario "Залог при плече": the leverage changes the margin only, the size stays the one of the amount.
    [Test]
    public async Task Margin_on_linear_is_the_amount_over_the_leverage_of_the_settings()
    {
        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, amount: 47.3m, leverage: 5), null, CancellationToken.None);

        (entry.Size.Quantity, entry.Size.Amount).ShouldBe((0.47m, 47.00m));
        (entry.Leverage, entry.Margin).ShouldBe((5, 9.40m));
    }

    // Scenario "Плечо на споте": the spot has no leverage and the exchange is not asked for one.
    [Test]
    public async Task Spot_enters_at_the_leverage_of_one_and_its_margin_is_the_amount()
    {
        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, amount: 50m, leverage: 5, category: MarketCategory.Spot, keyed: true), null, CancellationToken.None);

        (entry.Leverage, entry.Margin).ShouldBe((1, entry.Size.Amount));
        await _ledger.DidNotReceive().GetLinearPositionAsync(Arg.Any<ExchangeKey>(), Arg.Any<Instrument>(), Arg.Any<CancellationToken>());
    }

    // Scenario "Чужая позиция с другим плечом".
    [Test]
    public async Task Position_on_the_symbol_with_another_leverage_is_named_with_both_leverages()
    {
        _ledger.GetLinearPositionAsync(Key, Instrument(), Arg.Any<CancellationToken>()).Returns(new LinearPosition(1m, 0.5m));

        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, leverage: 5, keyed: true), null, CancellationToken.None);

        entry.Reasons.ShouldBe([IdeaTradeRefusal.LeverageDiffers]);
        entry.Detail.ShouldBe(RefusalDetail.Numbers(1m, 5m));
    }

    // Scenario "Открытый ордер с другим плечом".
    [Test]
    public async Task Open_order_on_the_symbol_with_another_leverage_is_named()
    {
        _ledger.GetOpenOrdersAsync(Key, Instrument(), Arg.Any<CancellationToken>()).Returns([new ExchangeOrder("c-1", "x-1", ExchangeOrderState.Open, 0m, null)]);

        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, leverage: 5, keyed: true), null, CancellationToken.None);

        entry.Reasons.ShouldBe([IdeaTradeRefusal.LeverageDiffers]);
    }

    // Scenario "Пустой символ с другим плечом": the leverage of the symbol is read and set at the entry.
    [Test]
    public async Task Empty_symbol_with_another_leverage_is_no_reason()
    {
        _ledger.GetLinearPositionAsync(Key, Instrument(), Arg.Any<CancellationToken>()).Returns(new LinearPosition(10m, 0m));

        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, leverage: 1, keyed: true), null, CancellationToken.None);

        entry.Reasons.ShouldBeEmpty();
    }

    // The trades of the account on the symbol that are open or wait count, with the leverage each entered with; those of another symbol or
    // another account do not.
    [Test]
    public async Task Symbol_carries_the_leverages_of_the_trades_of_the_account_on_it()
    {
        IdeaTrade unknown = Held() with { Leverage = null };
        var waiting = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Now, instrument: Instrument()), Account, 100m, new IdeaTradeSize(1m, 100m, 2m, 4m, 2m), Now, leverage: 3);
        IdeaTrade elsewhere = Held(Instrument("SOL"));
        IdeaTrade otherAccount = Held() with { AccountId = Guid.NewGuid() };
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([unknown, waiting, elsewhere, otherAccount]);

        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, keyed: true), null, CancellationToken.None);

        entry.Symbol!.Trades.ShouldBe([null, 3]);
    }

    // While a trade of the account stands on the symbol its leverage is never set, even when the exchange shows another.
    [Test]
    public async Task Leverage_is_not_set_while_a_trade_of_the_account_is_on_the_symbol()
    {
        var waiting = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Now, instrument: Instrument()), Account, 100m, new IdeaTradeSize(1m, 100m, 2m, 4m, 2m), Now, leverage: 5);
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([waiting]);
        _ledger.GetLinearPositionAsync(Key, Instrument(), Arg.Any<CancellationToken>()).Returns(new LinearPosition(10m, 0m));

        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, leverage: 5, keyed: true), null, CancellationToken.None);

        entry.LeverageToSet.ShouldBeNull();
    }

    // Review of add-idea-trade-leverage: the leverage and the position of the symbol come from one read of the exchange.
    [Test]
    public async Task Symbol_is_read_with_one_read_of_its_leverage_and_position()
    {
        _ledger.GetLinearPositionAsync(Key, Instrument(), Arg.Any<CancellationToken>()).Returns(new LinearPosition(1m, 0.5m));

        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, leverage: 5, keyed: true), null, CancellationToken.None);

        (entry.Symbol!.Leverage, entry.Symbol.Position).ShouldBe((1m, 0.5m));
        await _ledger.Received(1).GetLinearPositionAsync(Key, Instrument(), Arg.Any<CancellationToken>());
        await _ledger.DidNotReceiveWithAnyArgs().GetHoldingAsync(default!, default!, default!, default);
    }

    // The open orders of the symbol are asked for while its position is read, not after.
    [Test]
    public async Task Open_orders_are_read_while_the_position_is_read()
    {
        var position = new TaskCompletionSource<LinearPosition>();
        _ledger.GetLinearPositionAsync(Key, Instrument(), Arg.Any<CancellationToken>()).Returns(position.Task);

        Task<EntryCalculation> entry = _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, keyed: true), null, CancellationToken.None);

        await _ledger.Received(1).GetLinearPositionAsync(Key, Instrument(), Arg.Any<CancellationToken>());
        await _ledger.Received(1).GetOpenOrdersAsync(Key, Instrument(), Arg.Any<CancellationToken>());
        position.SetResult(new LinearPosition(1m, 0m));
        (await entry).Reasons.ShouldBeEmpty();
    }

    // A key that does not trade linear has its reason already: the symbol is not asked for.
    [Test]
    public async Task Account_that_cannot_trade_the_symbol_is_not_asked_for_its_leverage()
    {
        EntryCalculation entry = await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, keyed: true, access: AccountAccess.CategoryNotTraded), null, CancellationToken.None);

        entry.Reasons.ShouldBe([IdeaTradeRefusal.CategoryNotTraded]);
        await _ledger.DidNotReceive().GetLinearPositionAsync(Arg.Any<ExchangeKey>(), Arg.Any<Instrument>(), Arg.Any<CancellationToken>());
    }

    // Scenario "Ждущая сделка занимает свой залог": 100 USDT at 4× waits on another symbol and takes 25, the new 100 at 5× takes 20.
    [Test]
    public async Task Trade_that_waits_takes_its_own_margin()
    {
        var waiting = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Now, instrument: Instrument("SOL")), Account, 100m, new IdeaTradeSize(1m, 100m, 2m, 4m, 2m), Now, leverage: 4);
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([waiting]);
        _catalog.ListAsync(MarketCategory.Linear, Arg.Any<CancellationToken>())
            .Returns([new ListedInstrument(MarketCategory.Linear, "ETHUSDT", "ETH", "USDT"), new ListedInstrument(MarketCategory.Linear, "SOLUSDT", "SOL", "USDT")]);
        _ledger.GetLinearPositionAsync(Key, Instrument(), Arg.Any<CancellationToken>()).Returns(new LinearPosition(5m, 0m));

        _ledger.GetFreeAsync(Key, "USDT", Arg.Any<CancellationToken>()).Returns(40m);
        (await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, amount: 100m, leverage: 5, keyed: true), null, CancellationToken.None)).Reasons.ShouldBe([IdeaTradeRefusal.NotEnoughMoney]);

        _ledger.GetFreeAsync(Key, "USDT", Arg.Any<CancellationToken>()).Returns(45m);
        (await _calculator.EvaluateAsync(Preparation(minRiskReward: 1.2m, amount: 100m, leverage: 5, keyed: true), null, CancellationToken.None)).Reasons.ShouldBeEmpty();
    }

    private static Instrument Instrument(string coin = "ETH")
    {
        return new Instrument(MarketCategory.Linear, $"{coin}USDT");
    }

    // The reward to risk after the fees is 1.32 with a stop at 98 and a target at 102.9 from 100.
    /// <param name="minRiskReward"></param>

    /// <param name="maxSameDirection"></param>
    /// <param name="amount"></param>
    /// <param name="autosBudget"></param>
    /// <param name="leverage"></param>
    /// <param name="category"></param>    /// <param name="keyed">With the account and its key; without, there is no account and no money to count.</param>
    /// <param name="access"></param>
    private static EntryPreparation Preparation(
        decimal minRiskReward = 1.5m,
        int? maxSameDirection = null,
        decimal amount = 300m,
        decimal? autosBudget = null,
        int leverage = 1,
        MarketCategory category = MarketCategory.Linear,
        bool keyed = false,
        AccountAccess access = AccountAccess.Ready)
    {
        Idea generated = IdeaGenerator.Generate(Now, instrument: new Instrument(category, "ETHUSDT"));
        Idea idea = generated with { Track = generated.Track with { Plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 102.9m) } };
        IdeaTradingSettings settings = IdeaTradingSettings.Default with
        {
            MinRiskReward = minRiskReward,
            MaxSameDirection = maxSameDirection,
            MaxOpenTrades = 10,
            MaxTotalRisk = 100m,
            MaxAmount = 500m,
            AutosBudget = autosBudget,
            Leverage = leverage
        };
        ExchangeAccount? account = keyed ? new ExchangeAccount(Account, "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear, MarketCategory.Spot }, Now) : null;

        return new EntryPreparation(idea, settings, account, keyed ? Key : null, access, 100m, 100m, Rules, Fees, amount, "USDT", "ETH", [], []);
    }

    private static IdeaBucketKey KeyOf(Idea idea)
    {
        return new IdeaBucketKey(idea.Setup, idea.Track.Plan.Type, idea.Interval, idea.Track.Plan.Side, idea.Instrument.Category);
    }

    private static AutoEntry Entry(decimal minRiskReward, decimal? budget = null)
    {
        var conditions = new IdeaAutoConditions([IdeaSetup.Impulse], [IdeaType.TwoR], [Interval.FifteenMinutes], null, null, null, minRiskReward, 0);
        IdeaAuto auto = IdeaAuto.Create(Guid.NewGuid(), "Импульс лонг", Account, 300m, conditions, 0) with { Mode = IdeaAutoMode.Live, Budget = budget };

        return new AutoEntry(auto, minRiskReward, new DailyLossState(-19.05m, 30m, null, null), [], new Dictionary<Guid, IdeaAuto> { [auto.Id] = auto });
    }

    // An open trade of the account of 50 USDT, manual or of an auto.
    private static IdeaTrade Held(Instrument? instrument = null, Guid? autoId = null)
    {
        var size = new IdeaTradeSize(0.47m, 50m, 0.2m, 0.368m, 1.84m);
        var trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Now, instrument: instrument ?? Instrument()), Account, 106m, size, Now, autoId: autoId);

        return trade.EntryGrown(new LegTotals(0.47m, 49.82m, 0.02m), Now);
    }
}