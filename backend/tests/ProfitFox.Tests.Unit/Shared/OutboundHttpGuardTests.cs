using Shouldly;

namespace ProfitFox.Tests.Unit.Shared;

// No test may reach a real exchange: a client the test did not replace must fail instead of going online.
public class OutboundHttpGuardTests
{
    [Test]
    public void Outbound_requests_go_to_a_dead_proxy()
    {
        HttpClient.DefaultProxy.GetProxy(new Uri("https://api.bybit.com/v5/market/kline"))
            .ShouldBe(new Uri("http://127.0.0.1:9/"));
    }

    [Test]
    public void Local_requests_bypass_the_proxy()
    {
        HttpClient.DefaultProxy.IsBypassed(new Uri("http://localhost:5242/")).ShouldBeTrue();
    }
}
