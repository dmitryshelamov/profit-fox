using Grpc.Core;

using ProfitFox.Tests.Shared.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 6: the tests speak to an in-process stand-in of T-Invest, never to the real one.</summary>
public class MockTInvestApiTests
{
    [Test]
    public async Task Registered_method_answers_and_records_the_metadata_of_the_call()
    {
        MockTInvestApi api = new MockTInvestApi().On<GetAccountsRequest, GetAccountsResponse>("GetAccounts", _ => new GetAccountsResponse { Accounts = { new Account { Id = "2000000001" } } });
        var client = new UsersService.UsersServiceClient(api);
        var headers = new Metadata { { "authorization", "Bearer t.token" }, { "x-app-name", "profit-fox" } };

        GetAccountsResponse response = await client.GetAccountsAsync(new GetAccountsRequest(), headers);

        response.Accounts.Single().Id.ShouldBe("2000000001");
        RecordedCall call = api.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe("GetAccounts");
        call.Header("authorization").ShouldBe("Bearer t.token");
        call.Header("x-app-name").ShouldBe("profit-fox");
    }

    [Test]
    public async Task Failing_method_throws_the_status_with_its_trailers()
    {
        MockTInvestApi api = new MockTInvestApi().Fail("GetCandles", StatusCode.ResourceExhausted, "30042", new Metadata { { "x-ratelimit-reset", "7" } });
        var client = new MarketDataService.MarketDataServiceClient(api);

        RpcException exception = await Should.ThrowAsync<RpcException>(async () => await client.GetCandlesAsync(new GetCandlesRequest()));

        exception.StatusCode.ShouldBe(StatusCode.ResourceExhausted);
        exception.Status.Detail.ShouldBe("30042");
        exception.Trailers.GetValue("x-ratelimit-reset").ShouldBe("7");
    }

    [Test]
    public async Task Method_without_a_handler_is_unimplemented()
    {
        var client = new MarketDataService.MarketDataServiceClient(new MockTInvestApi());

        RpcException exception = await Should.ThrowAsync<RpcException>(async () => await client.GetCandlesAsync(new GetCandlesRequest()));

        exception.StatusCode.ShouldBe(StatusCode.Unimplemented);
    }

    [Test]
    public async Task Headers_of_an_answer_reach_the_caller()
    {
        MockTInvestApi api = new MockTInvestApi().On<GetCandlesRequest, GetCandlesResponse>("GetCandles", _ => new GetCandlesResponse(), new Metadata { { "x-ratelimit-limit", "300" } });
        var client = new MarketDataService.MarketDataServiceClient(api);

        using AsyncUnaryCall<GetCandlesResponse> call = client.GetCandlesAsync(new GetCandlesRequest());

        (await call.ResponseHeadersAsync).GetValue("x-ratelimit-limit").ShouldBe("300");
    }
}