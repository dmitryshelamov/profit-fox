using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Infrastructure.Exchanges.Retry;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     After the review: each exchange has a circuit breaker of its own, so a T-Invest that does not answer (six failures of one ladder of
///     retries of an import) never opens the circuit of Bybit, where the last prices of the autos and the warm-up of the bots go.
/// </summary>
public class ResilienceRegistrationTests
{
    private ProfitFoxFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = 0 };
        _factory.CreateClient().Dispose();
        _factory.TInvest.On<GetAccountsRequest, GetAccountsResponse>("GetAccounts", _ => new GetAccountsResponse { Accounts = { new Account { Id = "2000000001" } } });
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Open_circuit_of_tinvest_does_not_touch_bybit()
    {
        await OpenCircuitAsync(Exchange.TInvest);

        ExchangeUnavailableException refused = await Should.ThrowAsync<ExchangeUnavailableException>(() => GetAccountsAsync());
        refused.Message.ShouldContain("repeated failures");
        (await Executor(Exchange.Bybit).ExecuteOnceAsync(_ => ValueTask.FromResult(42), CancellationToken.None)).ShouldBe(42);
    }

    [Test]
    public async Task Open_circuit_of_bybit_does_not_touch_tinvest()
    {
        await OpenCircuitAsync(Exchange.Bybit);

        (await GetAccountsAsync()).Accounts.Single().Id.ShouldBe("2000000001");
        await Should.ThrowAsync<ExchangeUnavailableException>(() => Executor(Exchange.Bybit).ExecuteOnceAsync(_ => ValueTask.FromResult(42), CancellationToken.None).AsTask());
    }

    [Test]
    public async Task Worker_has_an_executor_per_exchange_too()
    {
        await using WorkerHost worker = _factory.StartWorker();

        worker.Services.GetRequiredKeyedService<ExchangeResilienceExecutor>(Exchange.Bybit)
            .ShouldNotBeSameAs(worker.Services.GetRequiredKeyedService<ExchangeResilienceExecutor>(Exchange.TInvest));
    }

    private ExchangeResilienceExecutor Executor(Exchange exchange)
    {
        return _factory.Services.GetRequiredKeyedService<ExchangeResilienceExecutor>(exchange);
    }

    private async Task OpenCircuitAsync(Exchange exchange)
    {
        for (var i = 0; i < 10; i++)
        {
            await Should.ThrowAsync<TransientExchangeException>(() => Executor(exchange).ExecuteOnceAsync<int>(_ => throw new TransientExchangeException("HTTP 503"), CancellationToken.None).AsTask());
        }
    }

    private Task<GetAccountsResponse> GetAccountsAsync()
    {
        TInvestCall call = _factory.Services.GetRequiredService<TInvestCall>();

        return call.OnceAsync("t.token", invoker => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest()), CancellationToken.None);
    }
}