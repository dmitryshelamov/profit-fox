using Grpc.Core;

using ProfitFox.Tests.Shared.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 12: the stand-in of T-Invest answers server and duplex streams from the script of a test, which can end or break them.</summary>
public class MockTInvestApiStreamTests
{
    [Test]
    public async Task Server_stream_answers_from_the_script_and_records_the_request_and_the_metadata()
    {
        var api = new MockTInvestApi();
        MockTInvestStream<OrderStateStreamRequest, OrderStateStreamResponse> script = api.OnServerStream<OrderStateStreamRequest, OrderStateStreamResponse>("OrderStateStream");
        var client = new OrdersStreamService.OrdersStreamServiceClient(api);

        using AsyncServerStreamingCall<OrderStateStreamResponse> call = client.OrderStateStream(new OrderStateStreamRequest { Accounts = { "2000000001" } }, new Metadata { { "authorization", "Bearer t.token" } });
        script.Send(new OrderStateStreamResponse { Ping = new Ping() });
        script.Complete();

        (await call.ResponseStream.MoveNext(CancellationToken.None)).ShouldBeTrue();
        call.ResponseStream.Current.PayloadCase.ShouldBe(OrderStateStreamResponse.PayloadOneofCase.Ping);
        (await call.ResponseStream.MoveNext(CancellationToken.None)).ShouldBeFalse();
        script.Requests.Single().Accounts.ShouldBe(["2000000001"]);
        api.Calls.Single().Header("authorization").ShouldBe("Bearer t.token");
    }

    [Test]
    public async Task Broken_stream_fails_with_the_status_and_the_next_call_opens_another()
    {
        var api = new MockTInvestApi();
        MockTInvestStream<OrderStateStreamRequest, OrderStateStreamResponse> script = api.OnServerStream<OrderStateStreamRequest, OrderStateStreamResponse>("OrderStateStream");
        var client = new OrdersStreamService.OrdersStreamServiceClient(api);
        using AsyncServerStreamingCall<OrderStateStreamResponse> first = client.OrderStateStream(new OrderStateStreamRequest());

        script.Fail(StatusCode.Unavailable);

        (await Should.ThrowAsync<RpcException>(() => first.ResponseStream.MoveNext(CancellationToken.None))).StatusCode.ShouldBe(StatusCode.Unavailable);
        using AsyncServerStreamingCall<OrderStateStreamResponse> second = client.OrderStateStream(new OrderStateStreamRequest());
        script.Send(new OrderStateStreamResponse { Ping = new Ping() });
        (await second.ResponseStream.MoveNext(CancellationToken.None)).ShouldBeTrue();
        script.Opens.ShouldBe(2);
    }

    [Test]
    public async Task Duplex_stream_records_what_the_client_writes_and_answers_from_the_script()
    {
        var api = new MockTInvestApi();
        MockTInvestStream<MarketDataRequest, MarketDataResponse> script = api.OnDuplex<MarketDataRequest, MarketDataResponse>("MarketDataStream");
        var client = new MarketDataStreamService.MarketDataStreamServiceClient(api);

        using AsyncDuplexStreamingCall<MarketDataRequest, MarketDataResponse> call = client.MarketDataStream();
        await call.RequestStream.WriteAsync(new MarketDataRequest { PingSettings = new PingDelaySettings { PingDelayMs = 30_000 } });
        script.Send(new MarketDataResponse { Ping = new Ping() });

        (await call.ResponseStream.MoveNext(CancellationToken.None)).ShouldBeTrue();
        script.Requests.Single().PingSettings.PingDelayMs.ShouldBe(30_000);
    }

    // The client gives a stream up with its token or by disposing the call.
    [Test]
    public async Task Stream_the_client_gives_up_is_no_longer_open()
    {
        var api = new MockTInvestApi();
        MockTInvestStream<OrderStateStreamRequest, OrderStateStreamResponse> script = api.OnServerStream<OrderStateStreamRequest, OrderStateStreamResponse>("OrderStateStream");
        var client = new OrdersStreamService.OrdersStreamServiceClient(api);
        using var cancellation = new CancellationTokenSource();
        AsyncServerStreamingCall<OrderStateStreamResponse> call = client.OrderStateStream(new OrderStateStreamRequest(), cancellationToken: cancellation.Token);
        script.IsOpen.ShouldBeTrue();

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => call.ResponseStream.MoveNext(CancellationToken.None));
        script.IsOpen.ShouldBeFalse();
        call.Dispose();
        client.OrderStateStream(new OrderStateStreamRequest()).Dispose();
        script.IsOpen.ShouldBeFalse();
    }

    [Test]
    public async Task Stream_without_a_script_is_unimplemented()
    {
        var client = new OrdersStreamService.OrdersStreamServiceClient(new MockTInvestApi());

        (await Should.ThrowAsync<RpcException>(async () => await Task.Run(() => client.OrderStateStream(new OrderStateStreamRequest())))).StatusCode.ShouldBe(StatusCode.Unimplemented);
    }
}