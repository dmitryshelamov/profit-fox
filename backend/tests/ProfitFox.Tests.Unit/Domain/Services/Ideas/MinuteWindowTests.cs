using System.Globalization;

using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Decision 1 of speed-up-ideas-replay: the window of the last minutes of an interval is kept as the minutes come, so the detector takes its
///     sum, low and high in one step instead of a pass over up to 1 440 minutes; the numbers are exactly those of the pass, scale included.
/// </summary>
public class MinuteWindowTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-01T10:00:00Z");

    [Test]
    public void Window_gives_the_sum_low_high_and_first_minute_of_the_last_minutes()
    {
        var window = new MinuteWindow(3);
        IdeaCandle[] minutes = [Minute(0, 100m, 101m, 99m, 100.5m, 10m), Minute(1, 100.5m, 102m, 100m, 101m, 20m), Minute(2, 101m, 101.5m, 98m, 99m, 5m), Minute(3, 99m, 103m, 98.5m, 102m, 7m)];

        foreach (IdeaCandle minute in minutes)
        {
            window.Add(minute);
        }

        (window.Count, window.First, window.Last, window.Sum, window.Low, window.High).ShouldBe((3, minutes[1], minutes[3], 32m, 98m, 103m));
    }

    [Test]
    public void Window_shorter_than_its_length_holds_every_minute_so_far()
    {
        var window = new MinuteWindow(3);
        window.Add(Minute(0, 100m, 101m, 99m, 100.5m, 10m));

        (window.Count, window.First, window.Sum, window.Low, window.High).ShouldBe((1, window.Last, 10m, 99m, 101m));
    }

    // The sum carries the scale of the minutes in the window, as a sum of them does: a minute of two decimals that left takes them with it.
    [Test]
    public void Sum_keeps_the_scale_of_the_minutes_in_the_window()
    {
        var window = new MinuteWindow(2);
        window.Add(Minute(0, 100m, 101m, 99m, 100m, 1.25m));
        window.Add(Minute(1, 100m, 101m, 99m, 100m, 2m));
        window.Add(Minute(2, 100m, 101m, 99m, 100m, 4m));

        window.Sum.ToString(CultureInfo.InvariantCulture).ShouldBe("6");
        window.Sum.ShouldBe(2m + 4m);
    }

    // The low and the high are the first of their value in the window, as Min and Max take them, with their scale.
    [Test]
    public void Low_and_high_are_the_first_of_equal_values_in_the_window()
    {
        var window = new MinuteWindow(3);
        window.Add(Minute(0, 100m, 101.0m, 99.0m, 100m, 1m));
        window.Add(Minute(1, 100m, 101.00m, 99.00m, 100m, 1m));
        window.Add(Minute(2, 100m, 100.5m, 99.5m, 100m, 1m));

        (window.Low.ToString(CultureInfo.InvariantCulture), window.High.ToString(CultureInfo.InvariantCulture)).ShouldBe(("99.0", "101.0"));

        window.Add(Minute(3, 100m, 100.5m, 99.5m, 100m, 1m));

        (window.Low.ToString(CultureInfo.InvariantCulture), window.High.ToString(CultureInfo.InvariantCulture)).ShouldBe(("99.00", "101.00"));
    }

    // Against the pass over the minutes on a long random walk: every step gives the sum, low and high of the last minutes.
    [Test]
    public void Window_agrees_with_a_pass_over_the_minutes_at_every_step()
    {
        var random = new Random(7);
        var window = new MinuteWindow(60);
        var minutes = new List<IdeaCandle>();
        decimal price = 100m;

        for (int index = 0; index < 500; index++)
        {
            decimal close = Math.Round(price * (1 + ((decimal)random.NextDouble() - 0.5m) / 50), random.Next(0, 4));
            minutes.Add(Minute(index, price, Math.Max(price, close) + (decimal)random.Next(0, 3) / 10, Math.Min(price, close) - (decimal)random.Next(0, 3) / 10, close, Math.Round((decimal)random.NextDouble() * 100, random.Next(0, 3))));
            price = close;
            window.Add(minutes[^1]);
            IdeaCandle[] last = [.. minutes.TakeLast(60)];

            (window.Sum.ToString(CultureInfo.InvariantCulture), window.Low.ToString(CultureInfo.InvariantCulture), window.High.ToString(CultureInfo.InvariantCulture), window.First).ShouldBe(
                (last.Sum(minute => minute.Volume).ToString(CultureInfo.InvariantCulture), last.Min(minute => minute.Low).ToString(CultureInfo.InvariantCulture), last.Max(minute => minute.High).ToString(CultureInfo.InvariantCulture), last[0]));
        }
    }

    private static IdeaCandle Minute(int offset, decimal open, decimal high, decimal low, decimal close, decimal volume)
    {
        return new IdeaCandle(Start.AddMinutes(offset), open, high, low, close, volume);
    }
}