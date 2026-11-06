using ProfitFox.Domain.ValueObjects.IdeaTrades;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.IdeaTrades;

public class LegTotalsTests
{
    [Test]
    public void Totals_add_and_subtract_by_quantity_value_and_fee()
    {
        var first = new LegTotals(0.2m, 4.7m, 0.002m);
        var second = new LegTotals(0.27m, 6.4m, 0.003m);

        (first + second).ShouldBe(new LegTotals(0.47m, 11.1m, 0.005m));
        (second - first).ShouldBe(new LegTotals(0.07m, 1.7m, 0.001m));
    }
}