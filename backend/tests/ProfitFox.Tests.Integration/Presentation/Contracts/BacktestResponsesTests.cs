using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Presentation.Contracts.Backtests;
using ProfitFox.Presentation.Contracts.Backtests.Converters;

namespace ProfitFox.Tests.Integration.Presentation.Contracts;

/// <summary>
///     Change fix-backtest-random-control, task 5.5 (design.md, decision 13): the numbers by weeks of a set and its mark with
///     the five conditions as the API gives them.
/// </summary>
public sealed class BacktestResponsesTests
{
    // «Плюс на сделку при минусе по неделям»: +0.24 % a trade, -0.14 % by weeks, 10 weeks up of 28, -1460 USDT without the
    // three best weeks, a difference of +0.02 % with a t of 0.1.
    private static readonly SetSummary Rest = new SetSummary(4265, 4265, 300, 0.0024, 1023.6, 0.48, Exclusions.None, 0, 0, 0, new RandomDifference(0.0002, 28, 0.1))
    {
        Weekly = new WeeklyNumbers(28, -0.0014, 10, 3, -1460)
    };

    [Test]
    public void A_set_of_a_backtest_on_the_rest_of_history_is_failed_with_its_five_conditions()
    {
        BacktestSetResponse set = BacktestResponses.Of(Backtest(Rest, engineVersion: 5, onRest: true)).Sets.Single();

        Assert.That(set.Mark!.Value, Is.EqualTo("failed"));
        Assert.That(set.Mark.Conditions, Is.EqualTo(new MarkConditionsResponse(Weeks: true, WeeklyMean: false, WeeksUp: false, WithoutBest: false, Difference: false)));
        Assert.That(set.Numbers!.Weekly, Is.EqualTo(new WeeklyNumbersResponse(28, -0.0014, 10, 3, -1460)));
    }

    [Test]
    public void A_set_with_the_same_numbers_of_a_backtest_without_a_source_has_no_mark()
    {
        BacktestSetResponse set = BacktestResponses.Of(Backtest(Rest, engineVersion: 5, onRest: false)).Sets.Single();

        Assert.That(set.Mark!.Value, Is.Null);
        Assert.That(set.Mark.Conditions.Weeks, Is.True, "the conditions are shown all the same");
    }

    [Test]
    public void A_set_of_engine_version_4_has_no_mark_and_no_numbers_by_weeks()
    {
        BacktestSetResponse set = BacktestResponses.Of(Backtest(Rest with { Weekly = null }, engineVersion: 4, onRest: true)).Sets.Single();

        Assert.That(set.Mark, Is.Null);
        Assert.That(set.Numbers!.Weekly, Is.Null);
    }

    [Test]
    public void A_set_of_a_strategy_for_a_bot_has_no_mark()
    {
        BacktestSetResponse set = BacktestResponses.Of(Backtest(Rest with { Weekly = null }, engineVersion: 5, onRest: false, StrategyKind.Bot)).Sets.Single();

        Assert.That(set.Mark, Is.Null);
    }

    [Test]
    public void A_pair_of_a_set_has_no_numbers_by_weeks()
    {
        var numbers = new SetNumbers { Orders = 1, Trades = 1, PairsWithTrades = 1, NetSum = 0.01, Amount = 1, Wins = 1, Weeks = [new WeekTrades(2921, 0.01, 1, 1)] };

        BacktestPairResponse pair = BacktestResponses.Of(new PairSummary("BTCUSDT", SetSummary.Of(numbers)));

        Assert.That(pair.Numbers.Weekly, Is.Null);
        Assert.That(BacktestResponses.Of(SetSummary.OfSet(numbers)).Weekly, Is.Not.Null, "the set of the same numbers has them");
    }

    private static BacktestInternal Backtest(SetSummary numbers, int engineVersion, bool onRest, StrategyKind kind = StrategyKind.Idea)
    {
        var settings = new BacktestSettings([], [TimeSpan.FromHours(4)], new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero), Pairs: null, 0.00055, 0.0002);
        return new BacktestInternal(
            "b",
            Guid.NewGuid(),
            "early-drop",
            1,
            kind,
            settings,
            [new BacktestSetOutcome(new BacktestSet(1, TimeSpan.FromHours(4), []), numbers)],
            1,
            onRest ? new BacktestSource("a", 7) : null,
            "done",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "w-1",
            3,
            3,
            engineVersion,
            "hash",
            1,
            1,
            Reason: null,
            numbers.Amount,
            "warning");
    }
}