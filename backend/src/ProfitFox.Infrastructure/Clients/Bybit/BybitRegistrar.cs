using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.Exchange;

namespace ProfitFox.Infrastructure.Clients.Bybit;

internal static class BybitRegistrar
{
    /// <summary>
    ///     The reader of the keys and balances of Bybit over a typed <see cref="HttpClient" />, at <c>Bybit:RestAddress</c> or
    ///     the live Bybit, and the clients of the trades. A test host replaces a port by registering its own afterwards.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        var settings = new BybitSettings(configuration["Bybit:RestAddress"], configuration["Bybit:SocketAddress"]);
        services.AddHttpClient<IExchangeKeyInspector, BybitAccountClient>((http, provider) =>
            new BybitAccountClient(http, settings, provider.GetService<ILogger<BybitAccountClient>>()));

        // The trades by ideas (change take-idea-trades): the public data, the calls that trade, and the two streams — that of
        // an account, built for each account, and that of the prices, one for the process.
        services.AddHttpClient<IExchangeMarket, BybitMarketClient>((http, provider) =>
            new BybitMarketClient(http, settings, provider.GetService<ILogger<BybitMarketClient>>()));
        services.AddHttpClient<IExchangeTrading, BybitTradingClient>((http, provider) =>
            new BybitTradingClient(http, settings, provider.GetService<ILogger<BybitTradingClient>>()));
        services.AddSingleton<IExchangeStream>(provider => new BybitAccountStream(settings, provider.GetService<ILogger<BybitAccountStream>>()));
        services.AddSingleton<IExchangePriceStream>(provider => new BybitPriceStream(settings, provider.GetService<ILogger<BybitPriceStream>>()));
    }
}
