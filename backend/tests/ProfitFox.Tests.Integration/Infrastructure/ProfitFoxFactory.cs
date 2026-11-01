using System.Globalization;

using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Presentation;

using Serilog.Core;

namespace ProfitFox.Tests.Integration.Infrastructure;

public class ProfitFoxFactory(string connectionString) : WebApplicationFactory<Program>
{
    public string ConnectionString => connectionString;

    public FakeBybitHandler Bybit { get; } = new FakeBybitHandler();

    /// <summary>The app's clock; it moves only when a test advances it.</summary>
    public FakeTimeProvider Time { get; } = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-10T12:30:00Z"));

    /// <summary>Workers started next to the API once it has migrated the database; none keeps created work <c>Pending</c>.</summary>
    public int Workers { get; init; } = 1;

    /// <summary>The Workers this factory started.</summary>
    public IReadOnlyList<WorkerHost> WorkerHosts => _workers;

    private readonly List<WorkerHost> _workers = [];

    /// <summary>The host environment: Testing reads appsettings.Testing.json and never the Development settings or user secrets.</summary>
    public string Environment { get; init; } = "Testing";

    /// <summary>Threads of a backtest in each Worker; without it the Worker takes its default.</summary>
    public int? BacktestThreads { get; init; }

    /// <summary>Receives every event the app's logger writes.</summary>
    public ILogEventSink? LogSink { get; init; }

    public static async Task<ProfitFoxFactory> CreateAsync(int workers = 1)
    {
        return new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = workers };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);

        // As in Development: every registered service must resolve, including handlers the API registers but never sends.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        builder.UseSetting("ConnectionStrings:MarketData", connectionString);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);

            // Bybit.Net configures its own primary handler, so replace the whole client: no test may reach the real Bybit.
            services.AddSingleton<IBybitRestClient>(_ => new BybitRestClient(
                new HttpClient(Bybit, disposeHandler: false),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })));

            if (LogSink is not null)
            {
                services.AddSingleton(LogSink);
            }
        });
    }

    /// <summary>Starts the Workers once the API host runs, so they find the database migrated.</summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = base.CreateHost(builder);

        for (var i = 0; i < Workers; i++)
        {
            WorkerHost worker = StartWorker();
            _workers.Add(worker);
        }

        return host;
    }

    /// <summary>A Worker of its own on the factory's database, fake Bybit and clock, started; the caller disposes it.</summary>
    /// <param name="services">Replaces services of the Worker after its own registration.</param>
    /// <param name="logSink">Receives every event the Worker's logger writes.</param>
    /// <param name="workerSettings">Settings over the Worker's own, such as <c>Worker:ChunkSize</c>.</param>
    public WorkerHost StartWorker(
        Action<IServiceCollection>? services = null,
        ILogEventSink? logSink = null,
        IReadOnlyDictionary<string, string?>? workerSettings = null)
    {
        Dictionary<string, string?> settings = workerSettings?.ToDictionary() ?? [];

        if (BacktestThreads is { } threads)
        {
            settings["Backtests:MaxParallelism"] = threads.ToString(CultureInfo.InvariantCulture);
        }

        var worker = new WorkerHost(connectionString, Bybit, Time, settings, services, logSink);
        worker.StartAsync().GetAwaiter().GetResult();

        return worker;
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (WorkerHost worker in _workers)
        {
            await worker.DisposeAsync();
        }

        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}