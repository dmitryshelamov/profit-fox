using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
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

    private readonly IIdeaTradeRepository _trades = Substitute.For<IIdeaTradeRepository>();
    private readonly IdeaTradeEntryCalculator _calculator;

    public IdeaTradeEntryCalculatorTests()
    {
        _trades.GetOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _calculator = new IdeaTradeEntryCalculator(
            Substitute.For<IIdeaRepository>(),
            Substitute.For<IIdeaTradingSettingsRepository>(),
            _trades,
            Substitute.For<IExchangeAccountRepository>(),
            Substitute.For<ISecretProtector>(),
            Substitute.For<IExchangeKeyInspector>(),
            Substitute.For<IExchangeLedger>(),
            Substitute.For<IMarketHistoryClient>(),
            Substitute.For<IInstrumentRulesProvider>(),
            Substitute.For<IInstrumentCatalog>(),
            Substitute.For<ILiveBotRepository>(),
            Substitute.For<IStrategyLibraryRepository>(),
            Substitute.For<IIdeaBucketRepository>(),
            new IdeasEngineVersion(IdeasEngineVersion.Current),
            new BacktestSettings());
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

    private static Instrument Instrument(string coin = "ETH")
    {
        return new Instrument(MarketCategory.Linear, $"{coin}USDT");
    }

    // The reward to risk after the fees is 1.32 with a stop at 98 and a target at 102.9 from 100.
    private static EntryPreparation Preparation(decimal minRiskReward = 1.5m, int? maxSameDirection = null, decimal amount = 300m, decimal? autosBudget = null)
    {
        Idea generated = IdeaGenerator.Generate(Now, instrument: Instrument());
        Idea idea = generated with { Track = generated.Track with { Plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 102.9m) } };
        IdeaTradingSettings settings = IdeaTradingSettings.Default with { MinRiskReward = minRiskReward, MaxSameDirection = maxSameDirection, MaxOpenTrades = 10, MaxTotalRisk = 100m, MaxAmount = 500m, AutosBudget = autosBudget };

        return new EntryPreparation(idea, settings, null, null, AccountAccess.Ready, 100m, 100m, Rules, Fees, amount, "USDT", "ETH", []);
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