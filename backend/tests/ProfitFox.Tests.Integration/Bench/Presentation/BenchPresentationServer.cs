using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ProfitFox.Bench.Application.MarketData;
using ProfitFox.Bench.Infrastructure.MarketData;
using ProfitFox.Bench.Presentation;
using ProfitFox.Tests.Integration.Bench.MarketData;

namespace ProfitFox.Tests.Integration.Bench.Presentation;

/// <summary>
///     The bench API in the test process, reading history from the fake market-data service.
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

    public static async Task<BenchPresentationServer> Start(MarketDataServer market, string benchFolder)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IHistorySource>(new GrpcHistorySource(market.Channel));
        builder.Services.AddSingleton<IInstrumentCatalog>(new InstrumentCatalog(market.Http));
        builder.Services.AddBenchPresentation(new BenchFolder(benchFolder));
        WebApplication app = builder.Build();
        app.MapControllers();
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
