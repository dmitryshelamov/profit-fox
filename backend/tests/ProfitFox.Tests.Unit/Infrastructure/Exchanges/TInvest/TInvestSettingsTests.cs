using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 7: the endpoint goes to the official host, or to a test host that resolves nowhere.</summary>
public class TInvestSettingsTests
{
    [Test]
    public void Official_host_is_the_default_and_passes()
    {
        var settings = new TInvestSettings();

        settings.Endpoint.ShouldBe("https://invest-public-api.tbank.ru:443");
        settings.Problem().ShouldBeNull();
    }

    [Test]
    public void Test_host_under_invalid_passes()
    {
        new TInvestSettings { Endpoint = "https://tinvest.invalid:443" }.Problem().ShouldBeNull();
    }

    [TestCase("https://sandbox-invest-public-api.tinkoff.ru:443")]
    [TestCase("https://evil.example.com")]
    public void Another_host_fails(string endpoint)
    {
        new TInvestSettings { Endpoint = endpoint }.Problem().ShouldNotBeNull().ShouldContain("TInvest:Endpoint");
    }

    [TestCase("http://invest-public-api.tbank.ru:443")]
    [TestCase("not a url")]
    public void Plain_http_or_a_malformed_address_fails(string endpoint)
    {
        new TInvestSettings { Endpoint = endpoint }.Problem().ShouldNotBeNull();
    }

    // After the review: a limit of zero or below would stop every request of the group for good, and the limiter would fail on an empty window.
    [TestCase(0)]
    [TestCase(-1)]
    public void Limit_not_above_zero_fails(int limit)
    {
        new TInvestSettings { Limits = new Dictionary<string, int> { ["MarketData"] = limit } }.Problem().ShouldNotBeNull().ShouldContain("TInvest:Limits:MarketData");
    }

    [Test]
    public void Limits_have_the_budget_of_each_group_of_methods()
    {
        new TInvestSettings().Limits.ShouldBe(new Dictionary<string, int> { ["Users"] = 50, ["Instruments"] = 150, ["MarketData"] = 100, ["Operations"] = 100, ["Orders"] = 50 });
    }
}