using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaAutos;

/// <summary>Requirement "Проверки учебного входа": 50 USDT at 100 with the stop at 98 and the target at 104 is 0.5 with a risk of 1.05445 and a reward to risk of 1.84.</summary>
public class PaperEntryTests
{
    private static readonly DateTimeOffset Fired = new(2026, 11, 7, 13, 40, 0, TimeSpan.Zero);
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.01m, 0.01m, 5m);
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly IdeaAuto Auto = Make("Пробои 5м", amount: 50m);

    [Test]
    public void Entry_that_breaks_nothing_is_sized_and_has_no_reason()
    {
        PaperEntryCheck check = Check();

        check.Refusals.ShouldBeEmpty();
        check.Size.ShouldBe(new IdeaTradeSize(0.5m, 50m, 1.05445m, check.Size!.Profit, check.Size.RiskReward));
        Math.Round(check.Size.RiskReward, 2).ShouldBe(1.84m);
    }

    [Test]
    public void Price_beyond_the_stop_or_the_target_is_named()
    {
        Check(close: 97.9m).Refusals.Select(refusal => refusal.Reason).ShouldContain(IdeaTradeRefusal.PriceBeyondStop);
        Check(close: 104.5m).Refusals.Select(refusal => refusal.Reason).ShouldContain(IdeaTradeRefusal.PriceBeyondTarget);
    }

    // A pullback enters at its limit: it is the market that may stand beyond a level already.
    [Test]
    public void Pullback_is_named_by_the_close_of_the_minute_not_by_its_limit()
    {
        Check(type: IdeaType.Pullback, close: 97.9m).Refusals.Select(refusal => refusal.Reason).ShouldContain(IdeaTradeRefusal.PriceBeyondStop);
        Check(type: IdeaType.Pullback, close: 101m).Refusals.ShouldBeEmpty();
    }

    [Test]
    public void Short_price_beyond_the_levels_is_named()
    {
        Check(side: IdeaSide.Short, close: 102.5m).Refusals.Select(refusal => refusal.Reason).ShouldContain(IdeaTradeRefusal.PriceBeyondStop);
        Check(side: IdeaSide.Short, close: 95m).Refusals.Select(refusal => refusal.Reason).ShouldContain(IdeaTradeRefusal.PriceBeyondTarget);
    }

    // Scenario "R:R ниже минимума": the auto wants 1.2, the target at 101.5 gives about 0.66.
    [Test]
    public void Risk_reward_below_the_minimum_of_the_auto_is_named_with_both_numbers()
    {
        PaperEntryCheck check = Check(target: 101.5m);

        check.Refusals.ShouldBe([new AutoRefusal(IdeaTradeRefusal.RiskRewardTooLow, RefusalDetail.Numbers(check.Size!.RiskReward, 1.2m))]);
        check.Size.RiskReward.ShouldBeLessThan(1.2m);
    }

    [Test]
    public void Amount_above_the_largest_is_named()
    {
        Check(auto: Make("Пробои 5м", amount: 600m)).Refusals.ShouldBe([new AutoRefusal(IdeaTradeRefusal.AmountAboveMax, RefusalDetail.Numbers(600m, 500m))]);
    }

    [Test]
    public void Quantity_and_amount_below_the_minimums_of_the_instrument_are_named()
    {
        Check(rules: Rules with { MinQuantity = 1m }).Refusals.Select(refusal => refusal.Reason).ShouldBe([IdeaTradeRefusal.QuantityBelowMinimum]);
        Check(rules: Rules with { MinAmount = 60m }).Refusals.Select(refusal => refusal.Reason).ShouldBe([IdeaTradeRefusal.AmountBelowMinimum]);
    }

    [Test]
    public void Unknown_rules_are_named_and_the_trade_has_no_size()
    {
        PaperEntryCheck check = Check(unknownRules: true);

        check.Refusals.Select(refusal => refusal.Reason).ShouldBe([IdeaTradeRefusal.RulesUnknown]);
        check.Size.ShouldBeNull();
    }

    [Test]
    public void Unknown_rules_do_not_hide_the_price_beyond_a_level()
    {
        Check(unknownRules: true, close: 97.9m).Refusals.Select(refusal => refusal.Reason).ShouldBe([IdeaTradeRefusal.PriceBeyondStop, IdeaTradeRefusal.RulesUnknown]);
    }

    // Scenario "Вторая идея той же пары".
    [Test]
    public void Pair_held_by_the_auto_itself_is_named()
    {
        PaperIdeaTrade held = Held(Auto, Fired.AddMinutes(-10));

        PaperEntryCheck check = Check(books: Books(paper: [held]));

        check.Refusals.Select(refusal => refusal.Reason).ShouldBe([IdeaTradeRefusal.PairTaken]);
        check.Refusals[0].Detail!.Holder!.Kind.ShouldBe(PairHolderKind.SameAuto);
    }

    // Scenario "Учебный при пустом аккаунте" and the like: the trades of other autos, manual ones, money and the guards do not matter.
    [Test]
    public void Trade_of_another_auto_or_a_manual_one_on_the_pair_does_not_stop_a_paper_trade()
    {
        PaperIdeaTrade other = Held(Make("Другой", 50m), Fired.AddMinutes(-10));
        IdeaTrade manual = Held(null, Fired.AddMinutes(-10)).Trade;

        Check(books: Books(real: [manual], paper: [other])).Refusals.ShouldBeEmpty();
    }

    // Scenario "Учебный упёрся в свой бюджет": its open paper trades on other pairs take 90 of 100, and the amount is 30.
    [Test]
    public void Budget_of_the_auto_taken_by_its_open_trades_is_named_with_what_is_taken()
    {
        IdeaAuto auto = Make("Пробои 5м", 30m) with { Budget = 100m };
        AutoBooks books = Books(paper: [Elsewhere(auto, 50m), Elsewhere(auto, 40m)]);

        PaperEntryCheck check = Check(auto, books: books);

        check.Refusals.ShouldBe([new AutoRefusal(IdeaTradeRefusal.BudgetTaken, RefusalDetail.Numbers(90m, 100m))]);
        Check(auto with { Budget = 120m }, books: books).Refusals.ShouldBeEmpty();
    }

    private static PaperEntryCheck Check(
        IdeaAuto? auto = null,
        IdeaType type = IdeaType.TwoR,
        IdeaSide side = IdeaSide.Long,
        decimal close = 100m,
        decimal? target = null,
        InstrumentRules? rules = null,
        bool unknownRules = false,
        AutoBooks? books = null)
    {
        Idea generated = IdeaGenerator.Generate(Fired, type: type, side: side);
        IdeaPlan plan = side == IdeaSide.Long ? new IdeaPlan(type, side, 100m, 98m, target ?? 104m) : new IdeaPlan(type, side, 100m, 102m, target ?? 96m);
        Idea idea = generated with { Track = generated.Track with { Plan = plan } };
        var birth = new IdeaCandle(Fired, close, close, close, close, 1m);

        return PaperEntry.Check(auto ?? Auto, idea, birth, unknownRules ? null : rules ?? Rules, Fees, 500m, books ?? Books());
    }

    private static AutoBooks Books(IReadOnlyCollection<IdeaTrade>? real = null, IReadOnlyCollection<PaperIdeaTrade>? paper = null)
    {
        return new AutoBooks(real ?? [], paper ?? [], new Dictionary<Guid, IdeaAuto> { [Auto.Id] = Auto }, "Bybit · ideas");
    }

    private static IdeaAuto Make(string name, decimal amount)
    {
        var conditions = new IdeaAutoConditions([IdeaSetup.Impulse], [IdeaType.TwoR], [Interval.FifteenMinutes], null, null, null, 1.2m, 0);

        return IdeaAuto.Create(Guid.NewGuid(), name, Account, amount, conditions, 0) with { Mode = IdeaAutoMode.Paper };
    }

    // An open paper trade of the auto on a pair of its own.
    private static PaperIdeaTrade Elsewhere(IdeaAuto auto, decimal amount)
    {
        var instrument = new Instrument(MarketCategory.Linear, $"A{Guid.NewGuid():N}USDT");
        var size = new IdeaTradeSize(1m, amount, 1m, 2m, 2m);
        var trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Fired.AddMinutes(-5), instrument: instrument), Account, amount, size, Fired, autoId: auto.Id);

        return new PaperIdeaTrade(trade, auto.Id, Fired);
    }

    private static PaperIdeaTrade Held(IdeaAuto? auto, DateTimeOffset at)
    {
        var size = new IdeaTradeSize(0.5m, 50m, 1.05445m, 1.9m, 1.8m);
        IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(at), Account, 100m, size, at, autoId: auto?.Id).EntryGrown(new LegTotals(0.5m, 50m, 0.0275m), at);

        return new PaperIdeaTrade(trade, auto?.Id ?? Guid.Empty, at);
    }
}