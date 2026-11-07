using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaAutos;

/// <summary>
///     Requirements "Одна сделка по паре у автомата", "Одна сделка с деньгами по паре аккаунта", "Не больше сделок в одну сторону", "Бюджет автомата" and
///     "Бюджет всех автоматов", and the daily stop.
/// </summary>
public class AutoRefusalsTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Bybit = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Other = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Instrument Eth = new Instrument(MarketCategory.Linear, "ETHUSDT");
    private static readonly Instrument Sui = new Instrument(MarketCategory.Linear, "SUIUSDT");

    private static readonly IdeaAuto Impulse = Auto("Импульс лонг", 0);
    private static readonly IdeaAuto Falls = Auto("Падения шорт", 1);
    private static readonly IdeaAuto Breakouts = Auto("Пробои 5м", 2);
    private static readonly IdeaAuto Gone = Auto("Старый", 3) with { DeletedAt = Day };

    private static readonly DailyLossState Calm = new DailyLossState(-19.05m, 30m, null, null);

    // Scenario "Вторая идея той же пары": the paper trade of the auto itself on DOGEUSDT since 12:50.
    [Test]
    public void Paper_trade_of_the_auto_holds_the_pair()
    {
        var doge = new Instrument(MarketCategory.Linear, "DOGEUSDT");
        PaperIdeaTrade held = Paper(Breakouts, doge, Day.AddHours(12).AddMinutes(50));

        AutoRefusal? refusal = AutoRefusals.PairTaken(Breakouts, doge, Books(paper: [held]));

        refusal.ShouldBe(new AutoRefusal(IdeaTradeRefusal.PairTaken, RefusalDetail.Pair(new PairHolder(PairHolderKind.SameAuto, "Пробои 5м", false, true, null, Day.AddHours(12).AddMinutes(50)))));
    }

    [Test]
    public void Real_trade_of_the_auto_holds_the_pair_on_any_account()
    {
        IdeaTrade held = Real(Breakouts, Eth, Other, Day.AddHours(9));

        AutoRefusal? refusal = AutoRefusals.PairTaken(Breakouts, Eth, Books(real: [held]));

        refusal!.Detail!.Holder.ShouldBe(new PairHolder(PairHolderKind.SameAuto, "Пробои 5м", false, false, null, Day.AddHours(9)));
    }

    [Test]
    public void Trade_that_waits_for_its_entry_holds_the_pair_since_it_was_made()
    {
        IdeaTrade waiting = Real(Breakouts, Eth, Bybit, Day.AddHours(8), open: false);

        AutoRefusals.PairTaken(Breakouts, Eth, Books(real: [waiting]))!.Detail!.Holder!.Since.ShouldBe(Day.AddHours(8));
    }

    [Test]
    public void Another_pair_or_market_is_no_holder()
    {
        PaperIdeaTrade other = Paper(Breakouts, Sui, Day.AddHours(9));
        PaperIdeaTrade spot = Paper(Breakouts, new Instrument(MarketCategory.Spot, "ETHUSDT"), Day.AddHours(9));

        AutoRefusals.PairTaken(Breakouts, Eth, Books(paper: [other, spot])).ShouldBeNull();
    }

    [Test]
    public void Trade_that_is_done_is_no_holder()
    {
        IdeaTrade done = Real(Breakouts, Eth, Bybit, Day.AddHours(9)) with { Status = IdeaTradeStatus.ClosedAtStop };

        AutoRefusals.PairTaken(Breakouts, Eth, Books(real: [done])).ShouldBeNull();
    }

    // Scenario "Верхний выигрывает": «Импульс лонг» took SUIUSDT on the account, «Падения шорт» is refused.
    [Test]
    public void Trade_of_a_higher_auto_on_the_account_holds_the_pair()
    {
        IdeaTrade held = Real(Impulse, Sui, Bybit, Day.AddHours(10));

        AutoRefusal? refusal = AutoRefusals.PairTaken(Falls, Sui, Books(real: [held], accountName: "Bybit · ideas"));

        refusal.ShouldBe(new AutoRefusal(IdeaTradeRefusal.PairTaken, RefusalDetail.Pair(new PairHolder(PairHolderKind.OtherAuto, "Импульс лонг", true, false, "Bybit · ideas", Day.AddHours(10)))));
    }

    [Test]
    public void Trade_of_a_lower_auto_holds_the_pair_too_and_is_not_above()
    {
        IdeaTrade held = Real(Breakouts, Sui, Bybit, Day.AddHours(10));

        AutoRefusals.PairTaken(Falls, Sui, Books(real: [held]))!.Detail!.Holder!.Above.ShouldBeFalse();
    }

    // Scenario "Ручная занимает пару": a manual trade on ETHUSDT since 13:05 on «Bybit · ideas».
    [Test]
    public void Manual_trade_on_the_account_holds_the_pair()
    {
        IdeaTrade manual = Real(null, Eth, Bybit, Day.AddHours(13).AddMinutes(5));

        AutoRefusal? refusal = AutoRefusals.PairTaken(Impulse, Eth, Books(real: [manual], accountName: "Bybit · ideas"));

        refusal!.Detail!.Holder.ShouldBe(new PairHolder(PairHolderKind.Manual, null, false, false, "Bybit · ideas", Day.AddHours(13).AddMinutes(5)));
    }

    // Scenario "Другой аккаунт".
    [Test]
    public void Trade_of_another_account_does_not_hold_the_pair()
    {
        IdeaTrade held = Real(Impulse, Sui, Other, Day.AddHours(10));

        AutoRefusals.PairTaken(Falls, Sui, Books(real: [held])).ShouldBeNull();
    }

    [Test]
    public void Paper_trade_of_another_auto_does_not_hold_the_pair()
    {
        AutoRefusals.PairTaken(Falls, Sui, Books(paper: [Paper(Impulse, Sui, Day.AddHours(9))])).ShouldBeNull();
    }

    [Test]
    public void The_trade_of_the_auto_itself_is_named_before_that_of_the_account()
    {
        IdeaTrade own = Real(Falls, Sui, Bybit, Day.AddHours(11));
        IdeaTrade higher = Real(Impulse, Sui, Bybit, Day.AddHours(10));

        AutoRefusals.PairTaken(Falls, Sui, Books(real: [higher, own]))!.Detail!.Holder!.Kind.ShouldBe(PairHolderKind.SameAuto);
    }

    [Test]
    public void The_earliest_trade_of_the_account_is_named()
    {
        IdeaTrade later = Real(Impulse, Sui, Bybit, Day.AddHours(12));
        IdeaTrade first = Real(null, Sui, Bybit, Day.AddHours(10));

        AutoRefusals.PairTaken(Falls, Sui, Books(real: [later, first]))!.Detail!.Holder!.Kind.ShouldBe(PairHolderKind.Manual);
    }

    [Test]
    public void Trade_of_a_deleted_auto_is_still_named()
    {
        IdeaTrade held = Real(Gone, Sui, Bybit, Day.AddHours(10));

        AutoRefusals.PairTaken(Falls, Sui, Books(real: [held]))!.Detail!.Holder!.AutoName.ShouldBe("Старый");
    }

    // Scenario "Пятый лонг".
    [Test]
    public void As_many_open_trades_of_the_side_as_allowed_is_named()
    {
        IReadOnlyList<IdeaTrade> longs = [.. Enumerable.Range(0, 5).Select(i => Real(i % 2 == 0 ? Impulse : null, new Instrument(MarketCategory.Linear, $"A{i}USDT"), i < 3 ? Bybit : Other, Day.AddHours(1)))];

        IReadOnlyList<AutoRefusal> refusals = AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 300m, Books(real: longs), Calm, 5, null);

        refusals.ShouldBe([new AutoRefusal(IdeaTradeRefusal.TooManySameDirection, RefusalDetail.Numbers(5m, 5m))]);
    }

    // Scenario "Шорт при пяти лонгах".
    [Test]
    public void Other_side_does_not_count()
    {
        IReadOnlyList<IdeaTrade> longs = [.. Enumerable.Range(0, 5).Select(i => Real(null, new Instrument(MarketCategory.Linear, $"A{i}USDT"), Bybit, Day.AddHours(1)))];

        AutoRefusals.Check(Impulse, Eth, IdeaSide.Short, 300m, Books(real: longs), Calm, 5, null).ShouldBeEmpty();
    }

    [Test]
    public void Fewer_open_trades_than_allowed_or_no_limit_is_no_reason()
    {
        IReadOnlyList<IdeaTrade> longs = [.. Enumerable.Range(0, 4).Select(i => Real(null, new Instrument(MarketCategory.Linear, $"A{i}USDT"), Bybit, Day.AddHours(1)))];

        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 300m, Books(real: longs), Calm, 5, null).ShouldBeEmpty();
        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 300m, Books(real: [.. longs, .. longs]), Calm, null, null).ShouldBeEmpty();
    }

    [Test]
    public void Paper_trades_are_not_counted_by_side()
    {
        IReadOnlyList<PaperIdeaTrade> paper = [.. Enumerable.Range(0, 5).Select(i => Paper(Breakouts, new Instrument(MarketCategory.Linear, $"A{i}USDT"), Day.AddHours(1)))];

        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 300m, Books(paper: paper), Calm, 5, null).ShouldBeEmpty();
    }

    // Scenario "Автомат при стопе".
    [Test]
    public void Daily_stop_that_holds_is_named_with_the_sum_and_the_limit()
    {
        var stopped = new DailyLossState(-30.15m, 30m, Day.AddHours(15).AddMinutes(12), Day.AddDays(1));

        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 300m, Books(), stopped, null, null).ShouldBe([new AutoRefusal(IdeaTradeRefusal.DailyStop, RefusalDetail.Numbers(-30.15m, 30m))]);
    }


    // Scenario "Бюджет занят": a paper trade of 50 and a real one of 30 are open, the amount is 30 and the budget 100.
    [Test]
    public void Budget_of_the_auto_taken_by_its_open_trades_is_named_with_what_is_taken()
    {
        IdeaAuto auto = Impulse with { Budget = 100m };
        AutoBooks books = Books(real: [Real(Impulse, Sui, Other, Day.AddHours(1), amount: 30m)], paper: [Paper(Impulse, Eth, Day.AddHours(2), amount: 50m)]);

        AutoRefusals.BudgetTaken(auto, 30m, books).ShouldBe(new AutoRefusal(IdeaTradeRefusal.BudgetTaken, RefusalDetail.Numbers(80m, 100m)));
    }

    // Scenario "Ровно в бюджет".
    [Test]
    public void Entry_that_fills_the_budget_exactly_goes()
    {
        IdeaAuto auto = Impulse with { Budget = 100m };

        AutoRefusals.BudgetTaken(auto, 30m, Books(real: [Real(Impulse, Sui, Bybit, Day.AddHours(1), amount: 70m)])).ShouldBeNull();
    }

    // Scenario "Автомат без бюджета".
    [Test]
    public void Auto_without_a_budget_is_never_refused_by_it()
    {
        AutoRefusals.BudgetTaken(Impulse, 30m, Books(real: [Real(Impulse, Sui, Bybit, Day.AddHours(1), amount: 1000m)])).ShouldBeNull();
    }

    [Test]
    public void Budget_of_the_auto_is_checked_with_the_amount_of_the_entry()
    {
        IdeaAuto auto = Impulse with { Budget = 100m };
        AutoBooks books = Books(real: [Real(Impulse, Sui, Bybit, Day.AddHours(1), amount: 70m)]);

        AutoRefusals.Check(auto, Eth, IdeaSide.Long, 31m, books, Calm, null, null).ShouldBe([new AutoRefusal(IdeaTradeRefusal.BudgetTaken, RefusalDetail.Numbers(70m, 100m))]);
        AutoRefusals.Check(auto, Eth, IdeaSide.Long, 30m, books, Calm, null, null).ShouldBeEmpty();
    }

    // Scenario "Бюджет автоматов занят": real trades of the autos of 100 and 80 are open, the budget of the autos is 200 and the amount 30.
    [Test]
    public void Budget_of_the_autos_taken_by_their_open_real_trades_is_named_with_what_is_taken()
    {
        AutoBooks books = Books(real: [Real(Falls, Sui, Bybit, Day.AddHours(1), amount: 100m), Real(Gone, Sui, Other, Day.AddHours(1), amount: 80m)]);

        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 30m, books, Calm, null, 200m)
            .ShouldBe([new AutoRefusal(IdeaTradeRefusal.AutosBudgetTaken, RefusalDetail.Numbers(180m, 200m))]);
        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 20m, books, Calm, null, 200m).ShouldBeEmpty();
    }

    // Scenario "Ручные не считаются".
    [Test]
    public void Manual_trades_take_nothing_of_the_budget_of_the_autos()
    {
        AutoBooks books = Books(real: [Real(null, Sui, Bybit, Day.AddHours(1), amount: 300m)]);

        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 30m, books, Calm, null, 200m).ShouldBeEmpty();
    }

    // Scenario "Учебные не занимают бюджет автоматов".
    [Test]
    public void Paper_trades_take_nothing_of_the_budget_of_the_autos()
    {
        AutoBooks books = Books(paper: [Paper(Falls, Sui, Day.AddHours(1), amount: 500m)]);

        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 30m, books, Calm, null, 200m).ShouldBeEmpty();
    }

    [Test]
    public void Budget_of_the_autos_that_is_off_stops_nothing()
    {
        AutoBooks books = Books(real: [Real(Falls, Sui, Bybit, Day.AddHours(1), amount: 1000m)]);

        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 30m, books, Calm, null, null).ShouldBeEmpty();
    }

    [Test]
    public void Reasons_come_in_a_fixed_order()
    {
        var stopped = new DailyLossState(-30.15m, 30m, Day.AddHours(15), Day.AddDays(1));
        IReadOnlyList<IdeaTrade> held = [Real(null, Eth, Bybit, Day.AddHours(1)), Real(Impulse, Sui, Bybit, Day.AddHours(1))];

        IReadOnlyList<AutoRefusal> refusals = AutoRefusals.Check(Impulse with { Budget = 300m }, Eth, IdeaSide.Long, 300m, Books(real: held), stopped, 2, 300m);

        refusals.Select(refusal => refusal.Reason).ShouldBe(
        [
            IdeaTradeRefusal.PairTaken,
            IdeaTradeRefusal.BudgetTaken,
            IdeaTradeRefusal.AutosBudgetTaken,
            IdeaTradeRefusal.DailyStop,
            IdeaTradeRefusal.TooManySameDirection
        ]);
    }

    [Test]
    public void Idea_that_breaks_nothing_has_no_reason()
    {
        AutoRefusals.Check(Impulse, Eth, IdeaSide.Long, 300m, Books(), Calm, 5, null).ShouldBeEmpty();
    }

    private static AutoBooks Books(IReadOnlyCollection<IdeaTrade>? real = null, IReadOnlyCollection<PaperIdeaTrade>? paper = null, string? accountName = null)
    {
        IdeaAuto[] autos = [Impulse, Falls, Breakouts, Gone];

        return new AutoBooks(real ?? [], paper ?? [], autos.ToDictionary(auto => auto.Id), accountName);
    }

    private static IdeaAuto Auto(string name, int position)
    {
        var conditions = new IdeaAutoConditions([IdeaSetup.Impulse], [IdeaType.TwoR], [Interval.FiveMinutes], null, null, null, 1.2m, 0);

        return IdeaAuto.Create(Guid.NewGuid(), name, Bybit, 300m, conditions, position) with { Mode = IdeaAutoMode.Live };
    }

    // A real trade open since the time, or one that waits for its entry.
    private static IdeaTrade Real(IdeaAuto? auto, Instrument instrument, Guid account, DateTimeOffset at, bool open = true, decimal amount = 50m)
    {
        var size = new IdeaTradeSize(0.47m, amount, 0.2m, 0.368m, 1.84m);
        var trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(at, instrument: instrument), account, 106m, size, at, autoId: auto?.Id);

        return open ? trade.EntryGrown(new LegTotals(0.47m, 49.82m, 0.02m), at) : trade;
    }

    private static PaperIdeaTrade Paper(IdeaAuto auto, Instrument instrument, DateTimeOffset since, decimal amount = 50m)
    {
        IdeaTrade trade = Real(auto, instrument, auto.AccountId, since, amount: amount);

        return new PaperIdeaTrade(trade, auto.Id, since);
    }
}