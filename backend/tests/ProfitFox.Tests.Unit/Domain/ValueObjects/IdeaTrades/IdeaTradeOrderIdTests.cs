using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.IdeaTrades;

/// <summary>The id an order of an idea trade carries to the exchange: it names the trade and the order and is never the id of a bot's order.</summary>
public class IdeaTradeOrderIdTests
{
    private static readonly Guid Trade = Guid.Parse("0f8c2f0e-5b1a-4c3d-9e7f-1a2b3c4d5e6f");

    [Test]
    public void Code_names_the_trade_and_the_order_and_reads_back()
    {
        var id = new IdeaTradeOrderId(Trade, 2);

        string code = id.Code;

        code.ShouldStartWith("i");
        code.ShouldEndWith("-2");
        IdeaTradeOrderId.TryParse(code, out IdeaTradeOrderId? read).ShouldBeTrue();
        read.ShouldBe(id);
    }

    // Bybit takes an orderLinkId of at most 36 characters.
    [Test]
    public void Code_fits_bybit_for_the_largest_values()
    {
        new IdeaTradeOrderId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), IdeaTradeOrderId.MaxNumber).Code.Length.ShouldBeLessThanOrEqualTo(36);
    }

    [Test]
    public void Number_past_the_largest_is_not_read()
    {
        IdeaTradeOrderId.TryParse($"{new IdeaTradeOrderId(Trade, 1).Code[..^1]}{IdeaTradeOrderId.MaxNumber + 1}", out _).ShouldBeFalse();
    }

    [Test]
    public void Code_of_a_trade_is_no_code_of_a_bot()
    {
        ClientOrderId.TryParse(new IdeaTradeOrderId(Trade, 1).Code, out _).ShouldBeFalse();
    }

    [Test]
    public void Code_of_a_bot_is_no_code_of_a_trade()
    {
        IdeaTradeOrderId.TryParse(new ClientOrderId(Trade, 1).Code, out _).ShouldBeFalse();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("manual-order")]
    [TestCase("i0000000000000000000000001-x")]
    [TestCase("i000000000000000000000000!-1")]
    [TestCase("i0000000000000000000000001-0")]
    [TestCase("izzzzzzzzzzzzzzzzzzzzzzzzz-1")]
    public void Foreign_code_is_not_read(string? code)
    {
        IdeaTradeOrderId.TryParse(code, out IdeaTradeOrderId? read).ShouldBeFalse();
        read.ShouldBeNull();
    }
}