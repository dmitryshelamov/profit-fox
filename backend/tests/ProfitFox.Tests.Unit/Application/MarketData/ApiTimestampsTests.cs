using ProfitFox.Application.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.MarketData;

public class ApiTimestampsTests
{
    [TestCase("2024-01-01T00:00:00Z", "2024-01-01T00:00:00+00:00")]
    [TestCase("2024-01-01T03:00:00+03:00", "2024-01-01T03:00:00+03:00")]
    [TestCase("2024-01-01T00:00:00.123Z", "2024-01-01T00:00:00.123+00:00")]
    public void Time_with_an_offset_is_accepted(string text, string expected)
    {
        ApiTimestamps.TryParse(text, out DateTimeOffset time).ShouldBeTrue();
        time.ShouldBe(DateTimeOffset.Parse(expected));
        time.Offset.ShouldBe(DateTimeOffset.Parse(expected).Offset);
    }

    // Scenario "Время без смещения": without an offset the value would depend on the server's time zone.
    [TestCase("2024-01-01T00:00:00")]
    [TestCase("2024-01-01")]
    [TestCase("yesterday")]
    public void Time_without_an_offset_is_rejected(string text)
    {
        ApiTimestamps.TryParse(text, out _).ShouldBeFalse();
    }
}