using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using TradeFlowProgram = ProfitFox.TradeFlow.Program;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     A collector of the trade flow with the registration of <c>ProfitFox.TradeFlow</c> on the database of a test, in the <c>Testing</c>
///     environment, with a fake Bybit for the stream and the archive and the test's clock. Each one is its own process for the lease.
/// </summary>
public sealed class TradeFlowHost : IAsyncDisposable
{
    private readonly IHost _host;

    /// <param name="connectionString">The database of the test, migrated by the API or the test.</param>
    /// <param name="sockets">Serves every Bybit stream of the collector.</param>
    /// <param name="archive">Serves the daily archive of trades.</param>
    /// <param name="time">The clock of the collector; it moves only when the test moves it.</param>
    /// <param name="settings">Settings over the collector's own.</param>
    public TradeFlowHost(
        string connectionString,
        FakeBybitSocketServer sockets,
        FakeBybitArchiveServer archive,
        TimeProvider time,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Testing",
            ContentRootPath = ContentRoot()
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MarketData"] = connectionString,
            ["Bybit:SocketAddress"] = sockets.Address,
            ["TradeFlow:ArchiveAddress"] = archive.Address
        });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        TradeFlowProgram.Configure(builder);
        builder.Services.AddSingleton(time);

        // As in Development: every registered service must resolve.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        _host = builder.Build();
    }

    public IServiceProvider Services => _host.Services;

    /// <summary>The folder of <c>ProfitFox.TradeFlow</c>, so the collector reads its own settings files and not those copied next to the tests.</summary>
    public static string ContentRoot()
    {
        return Path.Combine(Path.GetDirectoryName(ScannerHost.ContentRoot())!, "ProfitFox.TradeFlow");
    }

    public Task StartAsync()
    {
        return _host.StartAsync();
    }

    /// <summary>Stops the collector as a shutdown does: it keeps its lease until the lease expires.</summary>
    public Task StopAsync()
    {
        return _host.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}