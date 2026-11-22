using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.MarketData;

/// <summary>
///     Change add-ideas-and-market, task 2.1: the five timeframes of a request — <c>5m</c>, <c>15m</c>, <c>1h</c>, <c>4h</c>
///     and <c>1d</c> — and nothing else.
/// </summary>
public sealed class TimeframesTests
{
    [TestCase("5m", 5)]
    [TestCase("15m", 15)]
    [TestCase("1h", 60)]
    [TestCase("4h", 240)]
    [TestCase("1d", 1440)]
    public void A_timeframe_reads_and_writes_back(string name, int minutes)
    {
        Assert.That(Timeframes.TryParse(name, out TimeSpan length), Is.True);
        Assert.That(length, Is.EqualTo(TimeSpan.FromMinutes(minutes)));
        Assert.That(Timeframes.Name(length), Is.EqualTo(name));
    }

    [TestCase("2m")]
    [TestCase("1m")]
    [TestCase("30m")]
    [TestCase("2h")]
    [TestCase("")]
    [TestCase(null)]
    public void Another_text_is_not_a_timeframe(string? name)
    {
        Assert.That(Timeframes.TryParse(name, out _), Is.False);
    }

    [Test]
    public void The_list_goes_from_the_shortest()
    {
        Assert.That(Timeframes.All, Is.EqualTo(new[] { "5m", "15m", "1h", "4h", "1d" }));
    }
}
