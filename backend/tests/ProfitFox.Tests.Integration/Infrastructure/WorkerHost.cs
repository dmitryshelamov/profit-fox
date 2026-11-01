using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Serilog.Core;

using WorkerProgram = ProfitFox.Worker.Program;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     A Worker with the registration of <c>ProfitFox.Worker</c> on the database of a test, in the <c>Testing</c> environment, with a fake
///     Bybit and the test's clock. Each one is its own process for leases: a Worker of its own identity.
/// </summary>
public sealed class WorkerHost : IAsyncDisposable
{
    private readonly IHost _host;

    /// <param name="connectionString">The database of the test.</param>
    /// <param name="bybit">Answers every Bybit request of the Worker.</param>
    /// <param name="time">The clock of the Worker; it moves only when the test moves it.</param>
    /// <param name="settings">Settings over the Worker's own, such as <c>Backtests:MaxParallelism</c>.</param>
    /// <param name="services">Replaces services of the Worker after its own registration.</param>
    /// <param name="logSink">Receives every event the Worker's logger writes.</param>
    public WorkerHost(
        string connectionString,
        FakeBybitHandler bybit,
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
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = connectionString });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        WorkerProgram.Configure(builder);
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

        // As in Development: every registered service must resolve, including handlers the Worker registers but never sends.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        _host = builder.Build();
    }

    public IServiceProvider Services => _host.Services;

    /// <summary>The folder of <c>ProfitFox.Worker</c>, so the Worker reads its own settings files and not those copied next to the tests.</summary>
    public static string ContentRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "src", "ProfitFox.Worker", "ProfitFox.Worker.csproj")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(folder!.FullName, "src", "ProfitFox.Worker");
    }

    public Task StartAsync()
    {
        return _host.StartAsync();
    }

    /// <summary>Stops the Worker as a shutdown does: its running work is cancelled and keeps its lease until the lease expires.</summary>
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