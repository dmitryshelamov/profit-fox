using Grpc.Core;

using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Integration.Infrastructure;

// No test may reach a real exchange: a client the test did not replace must fail instead of going online.
public class OutboundHttpGuardTests
{
    [Test]
    public void Outbound_requests_go_to_a_dead_proxy()
    {
        HttpClient.DefaultProxy.GetProxy(new Uri("https://api.bybit.com/v5/market/kline"))
            .ShouldBe(new Uri("http://127.0.0.1:9/"));
        HttpClient.DefaultProxy.GetProxy(new Uri($"https://{TInvestSettings.OfficialHost}:443"))
            .ShouldBe(new Uri("http://127.0.0.1:9/"));
    }

    // The gRPC channel of T-Invest runs on the same HTTP stack: a call that slipped past the mock dies at the dead proxy, not at T-Invest.
    [Test]
    public async Task Grpc_call_to_tinvest_dies_at_the_dead_proxy()
    {
        using var channel = new TInvestChannel(new TInvestSettings(), TInvestCertificates.None);
        var client = new UsersService.UsersServiceClient(channel.Invoker);

        RpcException exception = await Should.ThrowAsync<RpcException>(async () =>
            await client.GetAccountsAsync(new GetAccountsRequest(), deadline: DateTime.UtcNow.AddSeconds(10)));

        exception.StatusCode.ShouldBe(StatusCode.Unavailable);
    }
}