using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.IdeaTrades;

/// <summary>Requirements "Итоги журнала" and "Обещание против итога". A trade here risks 1 USDT, so its result in USDT is its result in R.</summary>
public class IdeaJournalTotalsTests
{
    private static readonly DateTimeOffset Opened = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    // Scenario "Итоги по фильтру": the results +1.8, −1.1 and +0.2 and one cancelled; the mean +0.3, s = √(4.22 / 2), the margin 1.96 · s / √3.
    [Test]
    public void Counts_the_outcomes_and_gives_the_mean_in_r_with_its_interval()
    {
        var totals = IdeaJournalTotals.Compute(
            [Closed(IdeaTradeStatus.ClosedAtTarget, 1.8m), Closed(IdeaTradeStatus.ClosedAtStop, -1.1m), Closed(IdeaTradeStatus.ClosedManually, 0.2m), Cancelled(), Failed()]);

        (totals.Targets, totals.Stops, totals.Manual, totals.Liquidated, totals.Adl, totals.Cancelled, totals.Failed).ShouldBe((1, 1, 1, 0, 0, 1, 1));
        totals.Result.ShouldBe(0.9m);
        totals.MeanR.ShouldBe(0.3m);
        totals.MeanRLow!.Value.ShouldBe(0.3m - 1.6437m, 0.0001m);
        totals.MeanRHigh!.Value.ShouldBe(0.3m + 1.6437m, 0.0001m);
    }

    // Scenario "Закрытые по сроку в итогах учебных": +1.8 at the target, −1.0 at the stop, −0.2 and +0.1 at expiry; the mean (1.8 − 1.0 − 0.2 + 0.1) / 4.
    [Test]
    public void Trades_closed_at_expiry_are_counted_and_go_into_the_mean_and_the_money()
    {
        var totals = IdeaJournalTotals.Compute(
            [Closed(IdeaTradeStatus.ClosedAtTarget, 1.8m), Closed(IdeaTradeStatus.ClosedAtStop, -1.0m), Closed(IdeaTradeStatus.ClosedAtExpiry, -0.2m), Closed(IdeaTradeStatus.ClosedAtExpiry, 0.1m)]);

        (totals.Targets, totals.Stops, totals.Manual, totals.Expired).ShouldBe((1, 1, 0, 2));
        (totals.Result, totals.MeanR).ShouldBe((0.7m, 0.175m));
    }

    // Scenario "Ликвидация в итогах": −1.0 at the stop, −0.24 liquidated and +0.5 closed by ADL; the mean (−1.0 − 0.24 + 0.5) / 3.
    [Test]
    public void Liquidated_trades_and_trades_closed_by_adl_are_counted_and_go_into_the_mean_the_money_and_the_fees()
    {
        var totals = IdeaJournalTotals.Compute(
        [
            Closed(IdeaTradeStatus.ClosedAtStop, -1.0m, fee: 0.01m, minutes: 30),
            Closed(IdeaTradeStatus.Liquidated, -0.24m, fee: 0.02m, minutes: 60),
            Closed(IdeaTradeStatus.ClosedByAdl, 0.5m, fee: 0.03m, minutes: 90)
        ]);

        (totals.Stops, totals.Liquidated, totals.Adl).ShouldBe((1, 1, 1));
        (totals.Result, totals.MeanR).ShouldBe((-0.74m, -0.74m / 3m));
        totals.Fees.ShouldBe(0.12m);
        totals.MeanTime.ShouldBe(TimeSpan.FromMinutes(60));
    }

    // Scenario "Ликвидированная с обещанием": promised −0.10 R, got −0.24 R.
    [Test]
    public void Liquidated_trade_with_a_promise_is_compared_with_its_promise()
    {
        var totals = IdeaJournalTotals.Compute([Closed(IdeaTradeStatus.Liquidated, -0.24m, promised: -0.1m)]);

        totals.Promise.ShouldBe(new PromiseComparison(1, -0.1m, -0.24m));
    }

    // Scenario "Одна сделка".
    [Test]
    public void One_closed_trade_gives_its_result_as_the_mean_without_an_interval()
    {
        var totals = IdeaJournalTotals.Compute([Closed(IdeaTradeStatus.ClosedAtTarget, 1.8m)]);

        (totals.MeanR, totals.MeanRLow, totals.MeanRHigh).ShouldBe((1.8m, null, null));
    }

    [Test]
    public void No_closed_trades_give_zeros_and_no_mean()
    {
        var totals = IdeaJournalTotals.Compute([Cancelled()]);

        (totals.Targets, totals.Stops, totals.Manual, totals.Expired, totals.Cancelled, totals.Result, totals.Fees).ShouldBe((0, 0, 0, 0, 1, 0m, 0m));
        (totals.MeanR, totals.MeanRLow, totals.MeanRHigh, totals.MeanTime).ShouldBe((null, null, null, null));
        (totals.Promise, totals.Idea).ShouldBe((new PromiseComparison(0, null, null), new IdeaComparison(0, null, null)));
    }

    [Test]
    public void Sums_the_fees_and_averages_the_time_from_the_open_to_the_close()
    {
        var totals = IdeaJournalTotals.Compute(
            [Closed(IdeaTradeStatus.ClosedAtTarget, 1.8m, fee: 0.02m, minutes: 60), Closed(IdeaTradeStatus.ClosedAtStop, -1.1m, fee: 0.03m, minutes: 90), Cancelled()]);

        totals.Fees.ShouldBe(0.1m);
        totals.MeanTime.ShouldBe(TimeSpan.FromMinutes(75));
    }

    // A trade an older Worker opened while the journal was being rolled out has no open time; it still counts, only not in the mean time.
    [Test]
    public void A_closed_trade_without_an_open_time_stays_out_of_the_mean_time_only()
    {
        var totals = IdeaJournalTotals.Compute(
            [Closed(IdeaTradeStatus.ClosedAtTarget, 1.8m, minutes: 60), Closed(IdeaTradeStatus.ClosedAtStop, -1.1m) with { OpenedAt = null }]);

        (totals.Targets, totals.Stops, totals.MeanR).ShouldBe((1, 1, 0.35m));
        totals.MeanTime.ShouldBe(TimeSpan.FromMinutes(60));
    }

    // Scenario "Обещали больше, чем получили": the promised +0.15, +0.12 and +0.18 R against +1.8, −1.1 and −1.1; a trade with no promise and one with no expectancy stay out.
    [Test]
    public void Compares_the_promised_expectancy_with_the_result_of_the_same_trades()
    {
        var totals = IdeaJournalTotals.Compute(
        [
            Closed(IdeaTradeStatus.ClosedAtTarget, 1.8m, promised: 0.15m),
            Closed(IdeaTradeStatus.ClosedAtStop, -1.1m, promised: 0.12m),
            Closed(IdeaTradeStatus.ClosedManually, -1.1m, promised: 0.18m),
            Closed(IdeaTradeStatus.ClosedAtTarget, 5m),
            Closed(IdeaTradeStatus.ClosedAtTarget, 5m) with { Promise = new IdeaPromise(null, null, null, null, 0.33m, 1.8m) },
            Cancelled() with { Promise = Promise(0.9m) }
        ]);

        totals.Promise.Count.ShouldBe(3);
        totals.Promise.Promised.ShouldBe(0.15m);
        totals.Promise.Got!.Value.ShouldBe(-0.1333m, 0.0001m);
    }

    // Scenario "Исполнение дороже идеи": the ideas gave +1.9 and −1.0 R, the trades +1.8 and −1.1; a trade whose idea never entered stays out.
    [Test]
    public void Compares_the_result_of_the_ideas_with_the_result_of_their_trades()
    {
        var totals = IdeaJournalTotals.Compute(
        [
            Closed(IdeaTradeStatus.ClosedAtTarget, 1.8m, idea: 1.9m),
            Closed(IdeaTradeStatus.ClosedAtStop, -1.1m, idea: -1.0m),
            Closed(IdeaTradeStatus.ClosedAtStop, -1.1m) with { IdeaOutcome = new TradedIdeaOutcome(IdeaStatus.Expired, Opened, null) },
            Closed(IdeaTradeStatus.ClosedAtStop, -1.1m)
        ]);

        totals.Idea.ShouldBe(new IdeaComparison(2, 0.45m, 0.35m));
    }

    private static IdeaTrade Closed(IdeaTradeStatus status, decimal r, decimal fee = 0m, int minutes = 30, decimal? promised = null, decimal? idea = null)
    {
        IdeaTradeLeg leg = status switch
        {
            IdeaTradeStatus.ClosedAtTarget => IdeaTradeLeg.TakeProfit,
            IdeaTradeStatus.ClosedAtStop => IdeaTradeLeg.StopLoss,
            IdeaTradeStatus.ClosedAtExpiry => IdeaTradeLeg.Expiry,
            IdeaTradeStatus.Liquidated => IdeaTradeLeg.Liquidation,
            IdeaTradeStatus.ClosedByAdl => IdeaTradeLeg.Adl,
            _ => IdeaTradeLeg.Manual
        };
        IdeaTrade trade = New()
            .EntryGrown(new LegTotals(1m, 100m, fee), Opened)
            .ExitGrown(leg, new LegTotals(1m, 100m + r + (2 * fee), fee), Opened.AddMinutes(minutes));

        return trade with
        {
            Promise = promised is { } expectancy ? Promise(expectancy) : null,
            IdeaOutcome = idea is { } result ? new TradedIdeaOutcome(IdeaStatus.Target, Opened, result) : null
        };
    }

    private static IdeaTrade Cancelled()
    {
        return New().EntryEnded(Opened);
    }

    private static IdeaTrade Failed()
    {
        return New().Fail("refused", Opened);
    }

    private static IdeaPromise Promise(decimal expectancy)
    {
        return new IdeaPromise(IdeaBadge.Up, 0.31m, expectancy, 412, 0.33m, 1.8m);
    }

    private static IdeaTrade New()
    {
        return IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Opened), Guid.NewGuid(), 100m, new IdeaTradeSize(1m, 100m, 1m, 1.8m, 1.8m), Opened);
    }
}