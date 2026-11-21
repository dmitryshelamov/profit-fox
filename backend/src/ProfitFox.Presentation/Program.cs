using Grpc.Net.Client;

using ProfitFox.Application.MarketData;
using ProfitFox.Infrastructure.MarketData;

namespace ProfitFox.Presentation;

/// <summary>
///     The web API of the bench: the hypotheses, the queue of runs, the signal preview and the signal constructor for the page
///     of the frontend, and the frontend itself when it is built into <c>wwwroot</c>, over the database of the bench. At start
///     it migrates the database, which starts empty. History is read from the market-data service.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        var grpc = new Uri(Setting(builder.Configuration, "MarketData:GrpcUrl"));
        var rest = new Uri(Setting(builder.Configuration, "MarketData:RestUrl"));
        string connectionString = Setting(builder.Configuration, "ConnectionStrings:Bench");

        builder.Services.AddSingleton(_ => GrpcChannel.ForAddress(grpc, new GrpcChannelOptions { MaxReceiveMessageSize = 64 * 1024 * 1024 }));
        builder.Services.AddSingleton<IHistorySource>(services => new GrpcHistorySource(services.GetRequiredService<GrpcChannel>()));
        builder.Services.AddSingleton<IInstrumentCatalog>(_ => new InstrumentCatalog(new HttpClient { BaseAddress = rest, Timeout = TimeSpan.FromMinutes(1) }));
        builder.Services.AddBenchPresentation(connectionString);

        WebApplication app = builder.Build();
        app.PrepareBench();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapBench();

        await app.RunAsync();
    }

    private static string Setting(IConfiguration configuration, string key)
    {
        return configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"The setting {key} is not set.");
    }
}
