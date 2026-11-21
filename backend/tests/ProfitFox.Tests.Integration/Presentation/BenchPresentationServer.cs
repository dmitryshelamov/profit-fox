using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Infrastructure.MarketData;
using ProfitFox.Presentation;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     The bench API in the test process, over the test database and reading history from the fake market-data service.
/// </summary>
public sealed class BenchPresentationServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public HttpClient Http { get; }

    private BenchPresentationServer(WebApplication app)
    {
        _app = app;
        Http = app.GetTestClient();
    }

    /// <summary>
    ///     Starts the API over the migrated test database, as the tests that fill the database themselves want it.
    /// </summary>
    public static Task<BenchPresentationServer> Start(MarketDataServer market)
    {
        return Start(market, new BenchStart());
    }

    /// <summary>
    ///     Starts the API as <see cref="Program" /> does: when <see cref="BenchStart.Migrate" /> is set it migrates before
    ///     it serves.
    /// </summary>
    public static async Task<BenchPresentationServer> Start(MarketDataServer market, BenchStart start)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", WebRootPath = start.WebRoot });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IHistorySource>(new GrpcHistorySource(market.Channel));
        builder.Services.AddSingleton<IInstrumentCatalog>(new InstrumentCatalog(market.Http));
        builder.Services.AddBenchPresentation(start.ConnectionString ?? BenchDatabase.ConnectionString);
        WebApplication app = builder.Build();
        if (start.Migrate)
        {
            app.PrepareBench();
        }

        app.MapBench();
        await app.StartAsync();
        return new BenchPresentationServer(app);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
