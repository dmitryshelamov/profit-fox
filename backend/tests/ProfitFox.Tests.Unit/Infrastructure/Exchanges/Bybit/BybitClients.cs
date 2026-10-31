using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Retry;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>The adapter over Bybit.Net with HTTP served by the stub; the SDK rate limiter is off, it would wait in real time.</summary>
public static class BybitClients
{
    internal static BybitMarketDataClient Create(StubHttpHandler handler, TimeProvider time) =>
        Create(handler, new ExchangeResilienceExecutor(time));

    internal static BybitMarketDataClient Create(StubHttpHandler handler, ExchangeResilienceExecutor resilience) =>
        new(
            new BybitRestClient(handler.CreateClient(), NullLoggerFactory.Instance, Options.Create(new BybitRestOptions { RateLimiterEnabled = false })),
            resilience);
}
