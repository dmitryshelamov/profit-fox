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

using NSubstitute;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Presentation;

using Serilog.Core;

namespace ProfitFox.Tests.Integration.Infrastructure;

public class ProfitFoxFactory(string connectionString) : WebApplicationFactory<Program>
{
    public string ConnectionString => connectionString;

    public FakeBybitHandler Bybit { get; } = new FakeBybitHandler();

    /// <summary>Answers the app's calls to Bybit with the key of an account; no test reaches the private Bybit API.</summary>
    public IExchangeKeyInspector KeyInspector { get; } = Substitute.For<IExchangeKeyInspector>();

    /// <summary>Answers the app's reads of what an account holds on the exchange.</summary>
    public IExchangeLedger Ledger { get; } = Substitute.For<IExchangeLedger>();

    /// <summary>Answers the app's calls to T-Invest with a token: the accounts it opens and their portfolios; no test reaches T-Invest.</summary>
    public IBrokerAccountReader BrokerAccounts { get; } = Substitute.For<IBrokerAccountReader>();

    /// <summary>
    ///     Whether <see cref="KeyInspector" /> answers the calls with an account key. Without it the app's own Bybit client sends them
    ///     through its HTTP pipeline to <see cref="Bybit" />.
    /// </summary>
    public bool FakeKeyInspector { get; init; } = true;

    /// <summary>Every logger of the app writes from Verbose, for a test that must see everything the app could log.</summary>
    public bool VerboseLogs { get; init; }

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

    /// <summary>
    ///     Whether the fake Bybit answers the app's Bybit calls. Without it the app builds its own Bybit client, for a test of the
    ///     configured address; the test must point <see cref="BybitRestAddress" /> at a listener of its own.
    /// </summary>
    public bool FakeBybit { get; init; } = true;

    /// <summary>The value of <c>Bybit:RestAddress</c>; without it the app uses the live Bybit.</summary>
    public string? BybitRestAddress { get; init; }

    /// <summary>The value of <c>Bybit:SocketAddress</c>, like <c>ws://127.0.0.1:5297</c>; without it the API's streams use the live Bybit.</summary>
    public string? BybitSocketAddress { get; init; }

    /// <summary>
    ///     The value of <c>ExchangeAccounts:MasterKey</c> over the environment's own; null keeps the one of the settings files, which
    ///     only Development and Testing have.
    /// </summary>
    public string? MasterKey { get; init; } = TestMasterKey;

    public static readonly string TestMasterKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());

    /// <summary>Receives every event the app's logger writes.</summary>
    public ILogEventSink? LogSink { get; init; }

    /// <summary>A factory on the test database, which is emptied before every test.</summary>
    public static ProfitFoxFactory Create(int workers = 1)
    {
        return new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = workers };
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

        if (MasterKey is not null)
        {
            builder.UseSetting("ExchangeAccounts:MasterKey", MasterKey);
        }

        if (VerboseLogs)
        {
            foreach (string logger in new[] { "Microsoft.AspNetCore", "Microsoft.Hosting.Lifetime", "FluentMigrator", "ProfitFox", "Serilog.AspNetCore.RequestLoggingMiddleware" })
            {
                builder.UseSetting($"Serilog:MinimumLevel:Override:{logger}", "Verbose");
            }

            builder.UseSetting("Serilog:MinimumLevel:Default", "Verbose");
        }

        if (BybitRestAddress is not null)
        {
            builder.UseSetting("Bybit:RestAddress", BybitRestAddress);
        }

        if (BybitSocketAddress is not null)
        {
            builder.UseSetting("Bybit:SocketAddress", BybitSocketAddress);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton(Ledger);
            services.AddSingleton(BrokerAccounts);
            if (FakeKeyInspector)
            {
                services.AddSingleton(KeyInspector);
            }
            else
            {
                services.AddHttpClient<IExchangeKeyInspector, BybitAccountClient>().ConfigurePrimaryHttpMessageHandler(() => new SharedHandler(Bybit));
            }

            // Bybit.Net configures its own primary handler, so replace the whole client: no test may reach the real Bybit.
            if (FakeBybit)
            {
                services.AddSingleton<IBybitRestClient>(_ => new BybitRestClient(
                    new HttpClient(Bybit, disposeHandler: false),
                    NullLoggerFactory.Instance,
                    Options.Create(new BybitRestOptions { RateLimiterEnabled = false })));
            }

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

    /// <summary>Lets the HTTP client factory rotate and dispose its handler without disposing the shared fake.</summary>
    private sealed class SharedHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _invoker = new HttpMessageInvoker(inner, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _invoker.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            _invoker.Dispose();
            base.Dispose(disposing);
        }
    }
}