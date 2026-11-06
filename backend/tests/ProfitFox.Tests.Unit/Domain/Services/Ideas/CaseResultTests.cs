using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirement "Итог случая" with the base fees of Bybit linear: maker 0.02 %, taker 0.055 %.</summary>
public class CaseResultTests
{
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
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

    private static IdeaTrack Closed(IdeaType type, IdeaSide side, decimal entry, decimal stop, decimal target, IdeaStatus status, decimal exit)
    {
        return new IdeaTrack(new IdeaPlan(type, side, entry, stop, target), Fired, 5, 24, 2m, null, status, Fired.AddMinutes(30), Fired, exit);
    }
}