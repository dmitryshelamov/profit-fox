using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Persistence.Files.HistoryCache;
using ProfitFox.Tests.Integration.Exchange;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Logging;

namespace ProfitFox.Tests.Integration.Worker;

/// <summary>
///     A Worker of the bench in the test process: the services of <c>ProfitFox.Worker</c> over the test database and the
///     fake market-data service, with a lease of 2 seconds renewed twice a second so that a test does not wait for 30.
/// </summary>
internal sealed class WorkerHost : IAsyncDisposable
{
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Renewal = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     The settings <c>Trading</c> of a trading Worker of a test (change take-idea-trades): the intervals of a second and
    ///     more made a few tenths, so a test does not wait for minutes.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> FastTrading = Trading();

    /// <summary>
    ///     The settings <c>Trading</c> of a trading Worker with <see cref="FastTrading" /> where the test gives nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Trading(
        string tick = "00:00:00.05",
        string poll = "00:00:00.2",
        string control = "00:00:30",
        string openWait = "00:00:00.5",
        string idle = "00:00:00.5",
        string closeWait = "00:00:00.6",
        string mark = "00:00:00.3",
        string backoff = "00:00:00.2")
    {
        return new Dictionary<string, string?>
        {
            ["Trading:Enabled"] = "true",
            ["Trading:Tick"] = tick,
            ["Trading:PollInterval"] = poll,
            ["Trading:ControlInterval"] = control,
            ["Trading:StreamOpenWait"] = openWait,
            ["Trading:StreamIdle"] = idle,
            ["Trading:CloseWait"] = closeWait,
            ["Trading:MarkInterval"] = mark,
            ["Trading:MaxBackoff"] = backoff
        };
    }

    private readonly IHost _host;

    private WorkerHost(IHost host, string name)
    {
        _host = host;
        Name = name;
    }

    public string Name { get; }

    public IServiceProvider Services => _host.Services;

    /// <summary>
    ///     Starts a Worker named <paramref name="name" />.
    /// </summary>
    /// <param name="server">The market-data service the Worker reads history from.</param>
    /// <param name="name">The name the Worker is shown by.</param>
    /// <param name="connectionString">The database; the migrated test database when null.</param>
    /// <param name="scanner">
    ///     Whether the Worker may keep the live data (change add-ideas-and-market): off unless a test of the scanner asks.
    /// </param>
    /// <param name="time">The clock of the Worker; that of the system when null.</param>
    /// <param name="runs">Whether the Worker takes runs of the queue (<c>Worker:TakeRuns</c>).</param>
    /// <param name="cache">
    ///     The folder of the cache of history of the Worker (change add-backtest-history-cache); none when null.
    /// </param>
    /// <param name="bybit">
    ///     The fake exchange in place of every port of Bybit (change take-idea-trades); with it the Worker may be the trading
    ///     one, at the intervals of <paramref name="trading" /> or those of <see cref="FastTrading" />.
    /// </param>
    /// <param name="trading">The settings <c>Trading</c> of the Worker; <see cref="FastTrading" /> when null.</param>
    /// <param name="masterKey">The master key of the secrets; that of the tests by default, empty for none.</param>
    /// <param name="log">The journal of the Worker, when the test reads it.</param>
    public static async Task<WorkerHost> Start(
        MarketDataServer server,
        string name,
        string? connectionString = null,
        bool scanner = false,
        TimeProvider? time = null,
        string? cache = null,
        bool runs = true,
        FakeBybit? bybit = null,
        IReadOnlyDictionary<string, string?>? trading = null,
        string masterKey = Presentation.BenchTestApplication.TestMasterKey,
        ListLogger? log = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (log is not null)
        {
            builder.Logging.AddProvider(new ListLoggerProvider(log));
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
        }

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExchangeAccounts:MasterKey"] = masterKey,
            ["Bybit:RestAddress"] = "http://localhost:1/",
            ["Bybit:SocketAddress"] = "ws://localhost:1/",
            ["ConnectionStrings:Bench"] = connectionString ?? BenchDatabase.ConnectionString,
            ["MarketData:GrpcUrl"] = "http://localhost:1",
            ["MarketData:RestUrl"] = "http://localhost:2/",
            ["Worker:Name"] = name,
            ["Worker:LeaseDuration"] = Lease.ToString(),
            ["Worker:LeaseRenewal"] = Renewal.ToString(),
            ["Worker:PollInterval"] = "00:00:00.1",
            ["Worker:SchemaPollInterval"] = "00:00:00.2",
            ["Worker:TakeRuns"] = runs ? "true" : "false",
            ["Scanner:Enabled"] = scanner ? "true" : "false",
            ["Scanner:TickInterval"] = "00:00:00.05",
            ["Scanner:ReconcileInterval"] = "00:00:00.2",
            ["Scanner:WarmupRetry"] = "00:00:00.2"
        });
        builder.Configuration.AddInMemoryCollection(bybit is null ? new Dictionary<string, string?> { ["Trading:Enabled"] = "false" } : trading ?? FastTrading);
        ProfitFox.Worker.Program.Configure(builder);
        bybit?.Use(builder.Services);
        MarketDataRegistrar.History(builder.Services, cache is null ? null : new HistoryCacheSettings(cache, 1L << 40), _ => new GrpcHistorySource(server.Channel));
        builder.Services.AddSingleton(Repeats.WithoutPauses);
        builder.Services.AddSingleton<IInstrumentCatalog>(new InstrumentCatalog(server.Http));
        builder.Services.AddSingleton<IRecentHistory>(new RestRecentHistory(server.Http));
        builder.Services.AddSingleton<IMarketFeed>(_ => new GrpcMarketFeed(server.Channel, new InstrumentCatalog(server.Http), TimeProvider.System, new MarketFeedSettings { RetryDelay = TimeSpan.FromMilliseconds(50) }));
        if (time is not null)
        {
            builder.Services.AddSingleton(time);
        }

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
