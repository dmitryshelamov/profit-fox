using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.Exchange;

namespace ProfitFox.Infrastructure.Clients.Bybit;

internal static class BybitRegistrar
{
    /// <summary>
    ///     The reader of the keys and balances of Bybit over a typed <see cref="HttpClient" />, at <c>Bybit:RestAddress</c> or
    ///     the live Bybit. A test host replaces the port by registering its own afterwards.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        var settings = new BybitSettings(configuration["Bybit:RestAddress"]);
        services.AddHttpClient<IExchangeKeyInspector, BybitAccountClient>((http, provider) =>
            new BybitAccountClient(http, settings, provider.GetService<ILogger<BybitAccountClient>>()));
    }
}
