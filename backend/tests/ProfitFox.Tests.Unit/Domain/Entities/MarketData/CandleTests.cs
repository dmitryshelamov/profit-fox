using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.MarketData;

public class CandleTests
{
    private static readonly Interval Hour = Interval.OneHour;

    // Scenario "Период включает текущий момент": now is 2024-03-10T12:30:00Z.
    [TestCase("2024-03-10T11:00:00Z", "2024-03-10T12:30:00Z", arg3: true)]
    [TestCase("2024-03-10T12:00:00Z", "2024-03-10T12:30:00Z", arg3: false)]
    [TestCase("2024-03-10T11:00:00Z", "2024-03-10T12:00:00Z", arg3: true)]
    public void Candle_is_closed_once_its_interval_has_ended(string openTime, string now, bool expected)
    {
        Candle candle = CandleGenerator.Generate(DateTimeOffset.Parse(openTime));

        candle.IsClosedAt(Hour, DateTimeOffset.Parse(now)).ShouldBe(expected);
    }
}