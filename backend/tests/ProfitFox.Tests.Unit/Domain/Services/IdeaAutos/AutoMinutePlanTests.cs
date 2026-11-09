using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaAutos;

/// <summary>
///     Requirements "Порядок идей минуты", "Лимит новых сделок стороны за минуту" and "Лимит открытых сделок стороны" (decision 3 of
///     add-burst-and-auto-limits): the order an auto takes the ideas of a minute in, and the ideas its limits skip.
/// </summary>
public class AutoMinutePlanTests
{
    private static readonly DateTimeOffset Minute = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static readonly IdeaAuto Paper = IdeaAuto.Create(
        Guid.NewGuid(),
        "Импульс",
        Account,
        30m,
        new IdeaAutoConditions([IdeaSetup.Impulse], [IdeaType.TwoR], [Interval.FifteenMinutes], null, null, null, 1.2m, 0),
        0) with
    { Mode = IdeaAutoMode.Paper };

    // Scenario "Лучшая идея первой": of the same risk on 1 USDT, 0.02, the idea promised +0.05 R before the one promised −0.10 R.
    [Test]
    public void Idea_of_the_highest_expectancy_comes_first()
    {
        AutoMinutePlan.Order([(Numbers(-0.10m, -0.002m), Shares(2m)), (Numbers(0.05m, 0.001m), Shares(1m))]).ShouldBe([1, 0]);
    }

    // Scenario "По деньгам, а не по R" (decision 6 of add-own-idea-numbers): −0.72 R loses −0.00216 on 1 USDT, less than −0.062 R at −0.0031.
    [Test]
    public void Ideas_go_by_the_expectancy_per_usdt_not_in_r()
    {
        AutoMinutePlan.Order([(Numbers(-0.062m, -0.0031m), Shares(2m)), (Numbers(-0.72m, -0.00216m), Shares(1m))]).ShouldBe([1, 0]);
    }

    // Scenario "Идея без корзины последней": an idea without an expectancy comes after one promised −0.20 R, whatever its reward to risk; ideas
    // without one go by their reward to risk among themselves.
    [Test]
    public void Idea_without_an_expectancy_comes_last()
    {
        AutoMinutePlan.Order([(Numbers(null, null), Shares(3m)), (Numbers(-0.20m, -0.004m), Shares(1m)), (Numbers(null, null), Shares(4m))]).ShouldBe([1, 2, 0]);
    }

    // Equal expectancies go by the net reward to risk, the highest first; equal ones keep the order of birth.
    [Test]
    public void Equal_expectancy_goes_by_the_net_reward_to_risk_then_by_birth()
    {
        AutoMinutePlan.Order([(Numbers(0.1m, 0.002m), Shares(1.5m)), (Numbers(0.1m, 0.002m), Shares(2.5m)), (Numbers(0.1m, 0.002m), Shares(1.5m))]).ShouldBe([1, 0, 2]);
    }

    // Scenario "Тридцать лонгов": with a limit of 2 a minute the auto takes two longs, and the 28 other longs are one skip of the minute limit
    // that names the first of them. The limit is of a side, so of the three shorts of the scenario it takes two as well and skips the third
    // apart: the requirement says no more trades of a side than the limit, and the scenario's three shorts would break it.
    [Test]
    public void Minute_limit_takes_as_many_ideas_of_a_side_as_it_allows()
    {
        Idea[] longs = [.. Enumerable.Range(0, 30).Select(_ => Idea(IdeaSide.Long))];
        Idea[] shorts = [.. Enumerable.Range(0, 3).Select(_ => Idea(IdeaSide.Short))];
        var plan = new AutoMinutePlan(Paper with { MaxPerMinutePerSide = 2 }, Books());

        Idea[] taken = Walk(plan, [.. longs.Take(10), .. shorts, .. longs.Skip(10)]);

        taken.ShouldBe([longs[0], longs[1], shorts[0], shorts[1]]);
        plan.Skips.ShouldBe([new LimitSkip(longs[2], IdeaTradeRefusal.MinuteLimit, 28, 2), new LimitSkip(shorts[2], IdeaTradeRefusal.MinuteLimit, 1, 2)]);
    }

    // Scenario "Лимит не достигнут".
    [Test]
    public void Idea_under_the_limit_is_taken_without_a_skip()
    {
        Idea idea = Idea(IdeaSide.Long);
        var plan = new AutoMinutePlan(Paper with { MaxPerMinutePerSide = 2 }, Books());

        Walk(plan, [idea]).ShouldBe([idea]);
        plan.Skips.ShouldBeEmpty();
    }

    // Scenario "Четвёртый лонг": three open longs of the auto, paper and real, are its limit; the short is taken, the two longs are one skip. A
    // long of another auto and a closed long of its own do not count.
    [Test]
    public void Open_limit_counts_the_open_trades_of_the_side_of_the_auto()
    {
        Idea[] longs = [Idea(IdeaSide.Long), Idea(IdeaSide.Long)];
        Idea shortIdea = Idea(IdeaSide.Short);
        AutoBooks books = Books(
            real: [Real(Paper.Id, IdeaSide.Long), Real(Guid.NewGuid(), IdeaSide.Long), Real(Paper.Id, IdeaSide.Long, open: false)],
            paper: [PaperOf(IdeaSide.Long), PaperOf(IdeaSide.Long), PaperOf(IdeaSide.Short, open: false)]);
        var plan = new AutoMinutePlan(Paper with { MaxOpenPerSide = 3 }, books);

        Walk(plan, [longs[0], shortIdea, longs[1]]).ShouldBe([shortIdea]);
        plan.Skips.ShouldBe([new LimitSkip(longs[0], IdeaTradeRefusal.SideLimit, 2, 3)]);
    }

    // Scenario "Лонг закрылся": two of the three longs are open, so one more is taken, and the next of the minute is skipped.
    [Test]
    public void Open_limit_counts_the_trades_taken_in_the_minute()
    {
        Idea[] longs = [Idea(IdeaSide.Long), Idea(IdeaSide.Long)];
        var plan = new AutoMinutePlan(Paper with { MaxOpenPerSide = 3 }, Books(paper: [PaperOf(IdeaSide.Long), PaperOf(IdeaSide.Long), PaperOf(IdeaSide.Long, open: false)]));

        Walk(plan, longs).ShouldBe([longs[0]]);
        plan.Skips.ShouldBe([new LimitSkip(longs[1], IdeaTradeRefusal.SideLimit, 1, 3)]);
    }

    // An idea the plan allows but the auto did not take (a refusal of the entry) does not count against the limit of the minute.
    [Test]
    public void Idea_allowed_and_not_taken_does_not_count()
    {
        Idea[] longs = [Idea(IdeaSide.Long), Idea(IdeaSide.Long)];
        var plan = new AutoMinutePlan(Paper with { MaxPerMinutePerSide = 1 }, Books());

        plan.Allows(longs[0]).ShouldBeTrue();
        plan.Allows(longs[1]).ShouldBeTrue();
        plan.Skips.ShouldBeEmpty();
    }

    // Without limits every idea is allowed and none is skipped.
    [Test]
    public void Auto_without_limits_skips_nothing()
    {
        Idea[] ideas = [.. Enumerable.Range(0, 30).Select(_ => Idea(IdeaSide.Long))];
        var plan = new AutoMinutePlan(Paper, Books(paper: [PaperOf(IdeaSide.Long)]));

        Walk(plan, ideas).ShouldBe(ideas);
        plan.Skips.ShouldBeEmpty();
    }

    // The ideas the plan allows, each taken.
    private static Idea[] Walk(AutoMinutePlan plan, IEnumerable<Idea> ideas)
    {
        var taken = new List<Idea>();

        foreach (Idea idea in ideas.Where(plan.Allows))
        {
            plan.Took(idea);
            taken.Add(idea);
        }

        return [.. taken];
    }

    // The own numbers of an idea with its expectancy in R and on 1 USDT of the amount.
    private static IdeaNumbers Numbers(decimal? expectancy, decimal? perUsdt)
    {
        return new IdeaNumbers(0m, 1m, null, null, null, expectancy, null, null, perUsdt);
    }

    // Shares of a reward to risk after the fees.
    private static IdeaShares Shares(decimal netRiskReward)
    {
        return new IdeaShares(netRiskReward / 100m, 0.01m, 0.01m);
    }

    private static Idea Idea(IdeaSide side)
    {
        return IdeaGenerator.Generate(Minute, instrument: new Instrument(MarketCategory.Linear, $"P{Guid.NewGuid():N}"[..8] + "USDT"), side: side);
    }

    private static AutoBooks Books(IReadOnlyCollection<IdeaTrade>? real = null, IReadOnlyCollection<PaperIdeaTrade>? paper = null)
    {
        return new AutoBooks(real ?? [], paper ?? [], new Dictionary<Guid, IdeaAuto> { [Paper.Id] = Paper });
    }

    private static IdeaTrade Real(Guid autoId, IdeaSide side, bool open = true)
    {
        var size = new IdeaTradeSize(0.47m, 30m, 0.2m, 0.368m, 1.84m);
        var trade = IdeaTrade.Create(Guid.NewGuid(), Idea(side), Account, 105m, size, Minute.AddHours(-1), autoId: autoId);

        return open ? trade : trade with { Status = IdeaTradeStatus.ClosedAtStop };
    }

    private static PaperIdeaTrade PaperOf(IdeaSide side, bool open = true)
    {
        return new PaperIdeaTrade(Real(Paper.Id, side, open), Paper.Id, Minute);
    }
}