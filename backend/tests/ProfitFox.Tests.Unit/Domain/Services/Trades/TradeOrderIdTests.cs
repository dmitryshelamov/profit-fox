using ProfitFox.Domain.Services.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Trades;

/// <summary>
///     Change take-idea-trades, task 3.1: the link id of an order of the bench at Bybit (design.md, decision 4).
/// </summary>
public sealed class TradeOrderIdTests
{
    [Test]
    public void An_id_is_t_with_25_characters_of_base_36_and_the_number_and_at_most_36_long()
    {
        string id = TradeOrderId.Format(Guid.NewGuid(), 1);
        Assert.That(id, Does.Match("^t[0-9a-z]{25}-1$"));
        Assert.That(TradeOrderId.Format(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), 999999999).Length, Is.LessThanOrEqualTo(36));
    }

    [Test]
    public void An_id_reads_back_to_its_trade_and_number()
    {
        var trade = Guid.NewGuid();
        Assert.That(TradeOrderId.Parse(TradeOrderId.Format(trade, 7)), Is.EqualTo((trade, 7)));
        Assert.That(TradeOrderId.Parse(TradeOrderId.Format(Guid.Empty, 1)), Is.EqualTo((Guid.Empty, 1)));
    }

    [Test]
    public void The_same_order_has_the_same_id()
    {
        var trade = Guid.NewGuid();
        string first = TradeOrderId.Format(trade, 2);
        Assert.That(TradeOrderId.Format(trade, 2), Is.EqualTo(first));
        Assert.That(TradeOrderId.Format(trade, 2), Is.Not.EqualTo(TradeOrderId.Format(trade, 1)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("manual-order-1")]
    [TestCase("t0000000000000000000000000-0")]
    [TestCase("t000000000000000000000000!-1")]
    [TestCase("x0000000000000000000000000-1")]
    [TestCase("t0000000000000000000000000-")]
    [TestCase("t0000000000000000000000000-01")]
    public void A_string_that_is_not_an_order_of_the_bench_does_not_read(string? linkId)
    {
        Assert.That(TradeOrderId.Parse(linkId), Is.Null);
    }
}
