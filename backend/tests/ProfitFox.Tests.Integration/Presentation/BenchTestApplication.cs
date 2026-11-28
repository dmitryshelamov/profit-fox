using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Persistence.Files.HistoryCache;
using ProfitFox.Presentation;
using ProfitFox.Tests.Integration.Exchange;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     The bench API in memory for a test, as <see cref="Program" /> starts it: the Testing environment, the database of
///     <paramref name="start" /> (the migrated test database by default), which it migrates at start, and history read from the
///     fake market-data service <paramref name="market" />, so no request reaches the real one; with <paramref name="time" />
///     the clock of the API and of the fake service is that one; with <paramref name="cache" /> history goes through the cache
///     of history in that folder (change add-backtest-history-cache). The master key is <see cref="TestMasterKey" /> unless
///     <paramref name="masterKey" /> gives another — empty for none (change add-exchange-accounts); Bybit is
///     <paramref name="exchange" /> when given, and otherwise an address no request reaches, never the live Bybit; with
///     <paramref name="bybit" /> every port of Bybit is that fake (change take-idea-trades).
/// </summary>
internal sealed class BenchTestApplication(
    MarketDataServer market,
    BenchStart? start = null,
    TimeProvider? time = null,
    string? cache = null,
    string masterKey = BenchTestApplication.TestMasterKey,
    IExchangeKeyInspector? exchange = null,
    FakeBybit? bybit = null) : WebApplicationFactory<Program>
{
    /// <summary>
    ///     The master key of the tests: the bytes 0 to 31 in base64. Not a secret of anything.
    /// </summary>
    public const string TestMasterKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private const string TestingEnvironment = "Testing";

    private HttpClient? _http;

    /// <summary>
    ///     A client of the API; the API starts with the first one.
    /// </summary>
    public HttpClient Http => _http ??= CreateClient();

    // The connection string comes from the container: its port is random, so no appsettings file can hold it.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestingEnvironment);
        builder.UseSetting("ConnectionStrings:Bench", start?.ConnectionString ?? BenchDatabase.ConnectionString);
        builder.UseSetting("ExchangeAccounts:MasterKey", masterKey);
        builder.UseSetting("Bybit:RestAddress", "http://localhost:1/");
        if (start?.WebRoot is { } webRoot)
        {
            builder.UseWebRoot(webRoot);
        }

        builder.ConfigureTestServices(services =>
        {
            MarketDataRegistrar.History(services, cache is null ? null : new HistoryCacheSettings(cache, 1L << 40), _ => new GrpcHistorySource(market.Channel));
            services.AddSingleton<IInstrumentCatalog>(new InstrumentCatalog(market.Http));
            services.AddSingleton<IRecentHistory>(provider => new RestRecentHistory(market.Http, market.Channel, provider.GetService<TimeProvider>() ?? TimeProvider.System));
            services.AddSingleton<IMarketFeed>(provider => new GrpcMarketFeed(market.Channel, new InstrumentCatalog(market.Http), provider.GetService<TimeProvider>() ?? TimeProvider.System, new MarketFeedSettings { RetryDelay = TimeSpan.FromMilliseconds(50) }));
            bybit?.Use(services);
            if (exchange is not null)
            {
                services.AddSingleton(exchange);
            }

            if (time is not null)
            {
                market.Data.Time = time;
                services.AddSingleton(time);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _http?.Dispose();
        }

        base.Dispose(disposing);
    }
}
