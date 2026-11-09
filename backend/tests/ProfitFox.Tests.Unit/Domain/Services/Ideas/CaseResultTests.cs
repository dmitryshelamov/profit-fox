using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirements "Итог случая" and "Проскальзывание идей" with the base fees of Bybit linear: maker 0.02 %, taker 0.055 %, and the slippage 0.05 %.</summary>
public class CaseResultTests
{
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
    private static readonly FeeRates Slipping = new FeeRates(0.02m, 0.055m, 0.05m);
    private static readonly DateTimeOffset Fired = DateTimeOffset.Parse("2024-03-01T10:01:00Z");

    // Scenario "Цель с комиссией": 2 R minus (0.00055 × 100 + 0.00055 × 104) / 2.
    [Test]
    public void Target_gives_its_reward_in_r_less_the_taker_fees_of_the_entry_and_the_exit()
    {
        CaseResult.R(Closed(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Target, 104m), Fees).ShouldBe(1.9439m);
    }

    [Test]
    public void Short_gains_as_the_price_falls()
    {
        CaseResult.R(Closed(IdeaType.TwoR, IdeaSide.Short, 100m, 102m, 96m, IdeaStatus.Target, 96m), Fees).ShouldBe(1.9461m);
    }

    // A pullback enters with a limit: maker on the entry, taker on the exit.
    [Test]
    public void Pullback_pays_the_maker_fee_on_its_entry()
    {
        CaseResult.R(Closed(IdeaType.Pullback, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Target, 104m), Fees).ShouldBe(1.9614m);
    }

    // Scenario "Стоп-вход с комиссией": entered with a stop order at 101 and stopped at 99; taker on both, (−2 − 0.0555 − 0.0545) / 2.
    [Test]
    public void Stop_entry_pays_the_taker_fee_on_its_entry_and_its_exit()
    {
        CaseResult.R(Closed(IdeaType.StopEntry, IdeaSide.Long, 101m, 99m, 105m, IdeaStatus.Stop, 99m), Fees).ShouldBe(-1.055m);
    }

    [Test]
    public void Expired_idea_gives_what_its_last_close_gives()
    {
        CaseResult.R(Closed(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Expired, 101m), Fees).ShouldBe(0.444725m);
    }

    // Requirement "Проскальзывание идей", scenario "Цель с комиссией": a market entry buys at 100.05 and the target sells at 103.948, the
    // taker fees on those prices, (103.948 − 100.05 − 0.0550275 − 0.0571714) / 2.
    [Test]
    public void Market_entry_and_target_exit_fill_worse_by_the_slippage()
    {
        CaseResult.R(Closed(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Target, 104m), Slipping).ShouldBe(1.892900550m);
    }

    // Scenario "Short по рынку до цели": the short sells its entry at 99.95 and buys its target back at 96.048,
    // (3.902 − 0.0549725 − 0.0528264) / 2.
    [Test]
    public void Short_fills_its_sell_lower_and_its_buy_higher_by_the_slippage()
    {
        CaseResult.R(Closed(IdeaType.TwoR, IdeaSide.Short, 100m, 102m, 96m, IdeaStatus.Target, 96m), Slipping).ShouldBe(1.897100550m);
    }

    // Scenario "Лимитный вход «Отката»": the limit rests on the book and fills at 100, the target still sells at 103.948,
    // (3.948 − 0.02 − 0.0571714) / 2.
    [Test]
    public void Pullback_enters_at_its_limit_without_the_slippage()
    {
        CaseResult.R(Closed(IdeaType.Pullback, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Target, 104m), Slipping).ShouldBe(1.9354143m);
    }

    // Scenario "Стоп-вход с комиссией": the stop order buys at 101.0505 and the stop sells at 98.9505, (−2.1 − 0.0555778 − 0.0544228) / 2.
    [Test]
    public void Stop_entry_and_stop_exit_fill_worse_by_the_slippage()
    {
        CaseResult.R(Closed(IdeaType.StopEntry, IdeaSide.Long, 101m, 99m, 105m, IdeaStatus.Stop, 99m), Slipping).ShouldBe(-1.105000275m);
    }

    // Scenario "Истекла по close": the close 101 sells at 100.9495, (0.8995 − 0.0550275 − 0.0555222) / 2.
    [Test]
    public void Expired_idea_sells_its_last_close_worse_by_the_slippage()
    {
        CaseResult.R(Closed(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Expired, 101m), Slipping).ShouldBe(0.3944751375m);
    }

    [Test]
    public void Pullback_without_an_entry_is_no_case()
    {
        var track = new IdeaTrack(new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 100m, 98m, 104m), Fired, 5, 24, 2m, 4, IdeaStatus.Expired, Fired.AddMinutes(20));

        CaseResult.R(track, Fees).ShouldBeNull();
    }

    // An idea of a pair that left trading expires with no close known: it entered, but has no exit to count.
    [Test]
    public void Idea_that_entered_and_expired_without_an_exit_price_has_no_result()
    {
        IdeaTrack track = Closed(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Expired, 101m) with { ExitPrice = null };

        CaseResult.R(track, Fees).ShouldBeNull();
    }

    // Requirement "Итог случая без издержек", scenario "Цель": the plan prices, without fees and slippage, give the target its +2 R.
    [Test]
    public void Gross_result_of_a_target_is_its_reward_at_the_plan_prices()
    {
        CaseResult.GrossR(Closed(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Target, 104m)).ShouldBe(2m);
    }

    // Scenario "Стоп".
    [Test]
    public void Gross_result_of_a_stop_is_minus_one_r()
    {
        CaseResult.GrossR(Closed(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m, IdeaStatus.Stop, 98m)).ShouldBe(-1m);
    }

    // Scenario "Истёк по close у short".
    [Test]
    public void Gross_result_of_a_short_that_expired_at_its_close()
    {
        CaseResult.GrossR(Closed(IdeaType.TwoR, IdeaSide.Short, 100m, 102m, 96m, IdeaStatus.Expired, 99m)).ShouldBe(0.5m);
    }

    // Scenario "Сигнал без входа".
    [Test]
    public void Pullback_without_an_entry_has_no_gross_result()
    {
        var track = new IdeaTrack(new IdeaPlan(IdeaType.Pullback, IdeaSide.Long, 100m, 98m, 104m), Fired, 5, 24, 2m, 4, IdeaStatus.Expired, Fired.AddMinutes(20));

        CaseResult.GrossR(track).ShouldBeNull();
    }

    private static IdeaTrack Closed(IdeaType type, IdeaSide side, decimal entry, decimal stop, decimal target, IdeaStatus status, decimal exit)
    {
        return new IdeaTrack(new IdeaPlan(type, side, entry, stop, target), Fired, 5, 24, 2m, null, status, Fired.AddMinutes(30), Fired, exit);
    }
}