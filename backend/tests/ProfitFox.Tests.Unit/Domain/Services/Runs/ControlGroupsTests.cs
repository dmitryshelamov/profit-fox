using System.Security.Cryptography;
using System.Text;

using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

using static ProfitFox.Tests.Unit.Domain.Services.Runs.Designs;

namespace ProfitFox.Tests.Unit.Domain.Services.Runs;

/// <summary>
///     Spec strategy-backtest «Контрольная группа недели» (change fix-backtest-random-control, task 2.1): the pairs of a week
///     whose first candle closed by its start, the first 20 of them in the order of the SHA-256 of
///     <c>&lt;start of the week, unix ms&gt;:&lt;symbol&gt;</c>.
/// </summary>
public sealed class ControlGroupsTests
{
    private static readonly DateTimeOffset March2 = Utc(2026, 3, 2);
    private static readonly long Week = Blocks.Of(March2, TimeBlock.Week);
    private static readonly string[] Thirty = [.. Enumerable.Range(1, 30).Select(number => $"P{number:D2}USDT")];

    [Test]
    public void A_pair_whose_first_candle_closed_after_the_start_of_the_week_is_not_in_its_group()
    {
        // The first 4h candle of SOLUSDT closed on Wednesday 2026-03-04.
        var firsts = Thirty.Take(5).ToDictionary(symbol => symbol, _ => Utc(2026, 1, 5));
        firsts["SOLUSDT"] = Utc(2026, 3, 4, 4);
        var groups = new ControlGroups(firsts, TimeBlock.Week, size: 20);

        Assert.That(groups.Of(Week), Has.None.EqualTo("SOLUSDT"));
        Assert.That(groups.Contains(Week, "SOLUSDT"), Is.False);
        Assert.That(groups.Of(Week + 1), Has.Some.EqualTo("SOLUSDT"), "with six pairs every pair with history is in the group of 2026-03-09");
        Assert.That(groups.Contains(Week + 1, "SOLUSDT"), Is.True);
    }

    [Test]
    public void A_first_candle_closed_exactly_at_the_start_of_the_week_counts()
    {
        var groups = new ControlGroups(new Dictionary<string, DateTimeOffset> { ["BTCUSDT"] = March2 }, TimeBlock.Week, size: 20);

        Assert.That(groups.Of(Week), Is.EqualTo(new[] { "BTCUSDT" }));
    }

    [Test]
    public void With_fewer_than_twenty_pairs_with_history_the_group_has_all_of_them()
    {
        Dictionary<string, DateTimeOffset> firsts = Thirty.ToDictionary(symbol => symbol, _ => Utc(2026, 4, 1));
        foreach (string symbol in Thirty.Take(12))
        {
            firsts[symbol] = Utc(2026, 1, 5);
        }

        var groups = new ControlGroups(firsts, TimeBlock.Week, size: 20);

        Assert.That(groups.Of(Week), Is.EquivalentTo(Thirty.Take(12)));
    }

    [Test]
    public void The_group_has_twenty_pairs_when_more_have_history()
    {
        var groups = new ControlGroups(Thirty.ToDictionary(symbol => symbol, _ => Utc(2026, 1, 5)), TimeBlock.Week, size: 20);

        Assert.That(groups.Of(Week), Has.Count.EqualTo(20).And.Unique);
        Assert.That(groups.Of(Week), Is.SubsetOf(Thirty));
    }

    [Test]
    public void The_group_does_not_depend_on_the_order_the_pairs_are_given_in()
    {
        var forward = new ControlGroups(Thirty.ToDictionary(symbol => symbol, _ => Utc(2026, 1, 5)), TimeBlock.Week, size: 20);
        var backward = new ControlGroups(Thirty.Reverse().ToDictionary(symbol => symbol, _ => Utc(2026, 1, 5)), TimeBlock.Week, size: 20);

        Assert.That(backward.Of(Week), Is.EqualTo(forward.Of(Week)));
        Assert.That(backward.Of(Week + 5), Is.EqualTo(forward.Of(Week + 5)));
    }

    [Test]
    public void Two_weeks_have_different_groups()
    {
        var groups = new ControlGroups(Thirty.ToDictionary(symbol => symbol, _ => Utc(2026, 1, 5)), TimeBlock.Week, size: 20);

        Assert.That(groups.Of(Week + 1), Is.Not.EquivalentTo(groups.Of(Week)));
    }

    [Test]
    public void The_group_of_the_week_of_2026_03_02_over_thirty_symbols_is_the_one_recorded()
    {
        var groups = new ControlGroups(Thirty.ToDictionary(symbol => symbol, _ => Utc(2026, 1, 5)), TimeBlock.Week, size: 20);

        Assert.That(groups.Of(Week), Is.EqualTo(Recorded));
    }

    [Test]
    public void The_order_of_the_draw_is_that_of_the_sha256_of_the_start_of_the_week_and_the_symbol()
    {
        string[] expected =
        [
            .. Thirty
                .OrderBy(symbol => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{March2.ToUnixTimeMilliseconds()}:{symbol}"))), StringComparer.Ordinal)
                .Take(20)
        ];
        var groups = new ControlGroups(Thirty.ToDictionary(symbol => symbol, _ => Utc(2026, 1, 5)), TimeBlock.Week, size: 20);

        Assert.That(groups.Of(Week), Is.EqualTo(expected));
    }

    // The group of the week of 2026-03-02 (1772409600000 ms) over P01USDT … P30USDT, in the order of the draw.
    private static readonly string[] Recorded =
    [
        "P19USDT", "P30USDT", "P27USDT", "P21USDT", "P26USDT", "P02USDT", "P13USDT", "P23USDT", "P10USDT", "P22USDT",
        "P20USDT", "P03USDT", "P29USDT", "P28USDT", "P01USDT", "P24USDT", "P12USDT", "P17USDT", "P16USDT", "P04USDT"
    ];
}