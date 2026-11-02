using ProfitFox.Domain.ValueObjects.LiveTrading;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.LiveTrading;

/// <summary>The id an order of a bot carries to the exchange: it names the bot and the order, so a resent order stays one order.</summary>
public class ClientOrderIdTests
{
    [Test]
    public void Code_names_the_bot_and_the_order_and_reads_back()
    {
        var id = new ClientOrderId(Guid.Parse("0f8c2f0e-5b1a-4c3d-9e7f-1a2b3c4d5e6f"), 42);

        string code = id.Code;

        code.ShouldEndWith("-42");
        ClientOrderId.TryParse(code, out ClientOrderId? read).ShouldBeTrue();
        read.ShouldBe(id);
    }

    // Bybit takes an orderLinkId of at most 36 characters.
    [Test]
    public void Code_fits_bybit_for_the_largest_values()
    {
        new ClientOrderId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), int.MaxValue).Code.Length.ShouldBeLessThanOrEqualTo(36);
    }

    [Test]
    public void Codes_of_two_bots_differ()
    {
        new ClientOrderId(Guid.Parse("00000000-0000-0000-0000-000000000001"), 1).Code
            .ShouldNotBe(new ClientOrderId(Guid.Parse("00000000-0000-0000-0000-000000000002"), 1).Code);
    }

    // An order the owner placed by hand, or one of another program, is no order of a bot.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("manual-order")]
    [TestCase("0000000000000000000000001-x")]
    [TestCase("000000000000000000000000!-1")]
    [TestCase("00000000000000000000000001-1")]
    [TestCase("0000000000000000000000001-0")]
    [TestCase("zzzzzzzzzzzzzzzzzzzzzzzzz-1")]
    public void Foreign_code_is_not_read(string? code)
    {
        ClientOrderId.TryParse(code, out ClientOrderId? read).ShouldBeFalse();
        read.ShouldBeNull();
    }
}