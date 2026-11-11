using System.Globalization;

using ProfitFox.Application.TradeFlow;
using ProfitFox.Domain.ValueObjects.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.TradeFlow;

/// <summary>Requirement "Начало и разрывы данных потока сделок" and decision 9: a break counts in the days of a pair not rebuilt from the archive.</summary>
public class TradeFlowGapsTests
{
    private static readonly DateTimeOffset Today = DateTimeOffset.Parse("2026-11-10T00:00:00Z", CultureInfo.InvariantCulture);

    // Scenario "Разрыв сегодня".
    [Test]
    public void Break_of_a_day_not_rebuilt_is_a_gap()
    {
        DateTimeOffset lost = Today.AddHours(14).AddMinutes(2);

        TradeFlowGaps.Of([new StreamBreak("trades", lost, lost.AddMinutes(3))], [], Today, Today.AddDays(1))
            .ShouldBe([(lost, lost.AddMinutes(3))]);
    }

    // Scenario "Разрыв вчера закрыт".
    [Test]
    public void Break_of_a_rebuilt_day_is_no_gap()
    {
        DateTimeOffset lost = Today.AddDays(-1).AddHours(14);

        TradeFlowGaps.Of([new StreamBreak("trades", lost, lost.AddMinutes(3))], [Today.AddDays(-1)], Today.AddDays(-1), Today.AddDays(1))
            .ShouldBeEmpty();
    }

    // Decision 9: a break over two days counts in the days not rebuilt only.
    [Test]
    public void Break_over_midnight_counts_in_the_day_not_rebuilt()
    {
        DateTimeOffset lost = Today.AddMinutes(-2);

        TradeFlowGaps.Of([new StreamBreak("trades", lost, Today.AddMinutes(3))], [Today.AddDays(-1)], Today.AddDays(-1), Today.AddDays(1))
            .ShouldBe([(Today, Today.AddMinutes(3))]);
        TradeFlowGaps.Of([new StreamBreak("trades", lost, Today.AddMinutes(3))], [], Today.AddDays(-1), Today.AddDays(1))
            .ShouldBe([(lost, Today), (Today, Today.AddMinutes(3))]);
    }

    [Test]
    public void Gap_is_cut_to_the_period()
    {
        DateTimeOffset from = Today.AddHours(14);

        TradeFlowGaps.Of([new StreamBreak("trades", from.AddMinutes(-5), from.AddMinutes(5))], [], from, from.AddHours(1))
            .ShouldBe([(from, from.AddMinutes(5))]);
    }
}