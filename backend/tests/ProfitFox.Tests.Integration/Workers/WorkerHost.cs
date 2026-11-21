using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.MarketData;
using ProfitFox.Infrastructure.MarketData;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>
///     A Worker of the bench in the test process: the services of <c>ProfitFox.Worker</c> over the test database and the
///     fake market-data service, with a lease of 2 seconds renewed twice a second so that a test does not wait for 30.
/// </summary>
internal sealed class WorkerHost : IAsyncDisposable
{
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Renewal = TimeSpan.FromSeconds(0.5);

    private readonly IHost _host;

    private WorkerHost(IHost host, string name)
    {
        _host = host;
        Name = name;
    }

    public string Name { get; }

    /// <summary>
    ///     Starts a Worker named <paramref name="name" />.
    /// </summary>
    /// <param name="server">The market-data service the Worker reads history from.</param>
    /// <param name="name">The name the Worker is shown by.</param>
    /// <param name="connectionString">The database; the migrated test database when null.</param>
    public static async Task<WorkerHost> Start(MarketDataServer server, string name, string? connectionString = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Bench"] = connectionString ?? BenchDatabase.ConnectionString,
            ["MarketData:GrpcUrl"] = "http://localhost:1",
            ["MarketData:RestUrl"] = "http://localhost:2/",
            ["Worker:Name"] = name,
            ["Worker:LeaseDuration"] = Lease.ToString(),
            ["Worker:LeaseRenewal"] = Renewal.ToString(),
            ["Worker:PollInterval"] = "00:00:00.1",
            ["Worker:SchemaPollInterval"] = "00:00:00.2"
        });
        ProfitFox.Worker.Program.Configure(builder);
        builder.Services.AddSingleton<IHistorySource>(new GrpcHistorySource(server.Channel));
        builder.Services.AddSingleton<IInstrumentCatalog>(new InstrumentCatalog(server.Http));
        IHost host = builder.Build();
        await host.StartAsync();
        return new WorkerHost(host, name);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
