using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Serilog.Core;

using ScannerProgram = ProfitFox.Scanner.Program;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     A scanner with the registration of <c>ProfitFox.Scanner</c> on the database of a test, in the <c>Testing</c> environment, with a
///     fake Bybit for REST and streams and the test's clock. Each one is its own process for the lease of the stream.
/// </summary>
public sealed class ScannerHost : IAsyncDisposable
{
    private readonly IHost _host;

    /// <param name="connectionString">The database of the test, migrated by the API or the test.</param>
    /// <param name="bybit">Answers every Bybit REST request of the scanner.</param>
    /// <param name="sockets">Serves every Bybit stream of the scanner.</param>
    /// <param name="time">The clock of the scanner; it moves only when the test moves it.</param>
    /// <param name="settings">Settings over the scanner's own, such as <c>Scanner:Symbols</c>.</param>
    /// <param name="services">Replaces services of the scanner after its own registration.</param>
    /// <param name="logSink">Receives every event the scanner's logger writes.</param>
    public ScannerHost(
        string connectionString,
        FakeBybitHandler bybit,
        FakeBybitSocketServer sockets,
        TimeProvider time,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? services = null,
        ILogEventSink? logSink = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Testing",
            ContentRootPath = ContentRoot()
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MarketData"] = connectionString,
            ["Bybit:SocketAddress"] = sockets.Address
        });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        ScannerProgram.Configure(builder);
        builder.Services.AddSingleton<TimeProvider>(time);

        // Bybit.Net configures its own primary handler, so replace the whole client: no test may reach the real Bybit.
        builder.Services.AddSingleton<IBybitRestClient>(_ => new BybitRestClient(
            new HttpClient(bybit, disposeHandler: false),
            NullLoggerFactory.Instance,
            Options.Create(new BybitRestOptions { RateLimiterEnabled = false })));

        if (logSink is not null)
        {
            builder.Services.AddSingleton(logSink);
        }

        services?.Invoke(builder.Services);

        // As in Development: every registered service must resolve, including handlers the scanner registers but never sends.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        _host = builder.Build();
    }

    public IServiceProvider Services => _host.Services;

    /// <summary>The folder of <c>ProfitFox.Scanner</c>, so the scanner reads its own settings files and not those copied next to the tests.</summary>
    public static string ContentRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "src", "ProfitFox.Scanner", "ProfitFox.Scanner.csproj")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(folder!.FullName, "src", "ProfitFox.Scanner");
    }

    public Task StartAsync()
    {
        return _host.StartAsync();
    }

    /// <summary>Stops the scanner as a shutdown does: it keeps its lease until the lease expires.</summary>
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