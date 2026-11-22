using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Presentation;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     The bench API in memory for a test, as <see cref="Program" /> starts it: the Testing environment, the database of
///     <paramref name="start" /> (the migrated test database by default), which it migrates at start, and history read from the
///     fake market-data service <paramref name="market" />, so no request reaches the real one; with <paramref name="time" />
///     the clock of the API is that one.
/// </summary>
internal sealed class BenchTestApplication(MarketDataServer market, BenchStart? start = null, TimeProvider? time = null) : WebApplicationFactory<Program>
{
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
        if (start?.WebRoot is { } webRoot)
        {
            builder.UseWebRoot(webRoot);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IHistorySource>(new GrpcHistorySource(market.Channel));
            services.AddSingleton<IInstrumentCatalog>(new InstrumentCatalog(market.Http));
            if (time is not null)
            {
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
