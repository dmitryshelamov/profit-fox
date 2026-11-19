using Grpc.Net.Client;
using ProfitFox.Bench.Application.MarketData;
using ProfitFox.Bench.Infrastructure.MarketData;

namespace ProfitFox.Bench.Presentation;

/// <summary>
///     The web API of the bench (change add-signal-preview): the signal preview for the page of the frontend. It runs on
///     the owner's machine and reads history from the market-data service, like <c>bench run</c>.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        Uri grpc = new Uri(Setting(builder.Configuration, "MarketData:GrpcUrl"));
        Uri rest = new Uri(Setting(builder.Configuration, "MarketData:RestUrl"));
        string folder = builder.Configuration["Bench:Folder"] is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(RepositoryRoot(), "bench");

        builder.Services.AddSingleton(_ => GrpcChannel.ForAddress(grpc, new GrpcChannelOptions { MaxReceiveMessageSize = 64 * 1024 * 1024 }));
        builder.Services.AddSingleton<IHistorySource>(services => new GrpcHistorySource(services.GetRequiredService<GrpcChannel>()));
        builder.Services.AddSingleton<IInstrumentCatalog>(_ => new InstrumentCatalog(new HttpClient { BaseAddress = rest, Timeout = TimeSpan.FromMinutes(1) }));
        builder.Services.AddBenchPresentation(new BenchFolder(folder));

        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapControllers();

        app.Run();
    }

    private static string Setting(IConfiguration configuration, string key)
    {
        return configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"The setting {key} is not set.");
    }

    // The repository the API runs in: up from the current folder, or from the binaries when started elsewhere.
    private static string RepositoryRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? folder = new DirectoryInfo(start);
            while (folder is not null && !Path.Exists(Path.Combine(folder.FullName, ".git")))
            {
                folder = folder.Parent;
            }

            if (folder is not null)
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("The bench API runs inside the profit-fox repository, or Bench:Folder is set.");
    }
}
