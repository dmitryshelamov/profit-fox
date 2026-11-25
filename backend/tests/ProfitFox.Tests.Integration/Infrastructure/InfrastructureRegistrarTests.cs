using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Application.Services.MarketData;
using ProfitFox.Infrastructure;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Persistence.Files.HistoryCache;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     The services <c>AddInfrastructure</c> gives are whole: every handler of the application can be built from them, in the API as
///     in the Worker, as a start in <c>Development</c> checks.
/// </summary>
public sealed class InfrastructureRegistrarTests
{
    [Test]
    public void Every_handler_of_the_application_can_be_built()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(BenchDatabase.Settings(BenchDatabase.ConnectionString));

        Assert.That(
            () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose(),
            Throws.Nothing);
        Assert.That(services.Count(service => service.ServiceType.IsGenericType && service.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)), Is.GreaterThan(0));
    }

    /// <summary>
    ///     Task 19.8: the queue of the live feeds is as long as <c>Scanner:QueueCapacity</c> says — one setting for both queues —
    ///     and 20 000 without it.
    /// </summary>
    [TestCase("1234", 1234)]
    [TestCase(null, 20_000)]
    public void The_queue_of_the_live_feeds_takes_the_capacity_of_the_scanner(string? capacity, int expected)
    {
        IConfiguration settings = new ConfigurationBuilder()
            .AddConfiguration(BenchDatabase.Settings(BenchDatabase.ConnectionString))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Scanner:QueueCapacity"] = capacity })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(settings);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<MarketFeedSettings>().QueueCapacity, Is.EqualTo(expected));
    }

    /// <summary>
    ///     Task 24.1: how long the stream of minutes may be silent is <c>Scanner:FeedSilence</c> — one setting for the watchdog
    ///     of the feed and the state of the scanner — and three minutes without it.
    /// </summary>
    [TestCase("00:05:00", 5)]
    [TestCase(null, 3)]
    public void The_silence_of_the_feed_takes_the_setting_of_the_scanner(string? silence, int minutes)
    {
        IConfiguration settings = new ConfigurationBuilder()
            .AddConfiguration(BenchDatabase.Settings(BenchDatabase.ConnectionString))
            .AddInMemoryCollection(silence is null ? [] : new Dictionary<string, string?> { ["Scanner:FeedSilence"] = silence })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(settings);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<MarketFeedSettings>().FeedSilence, Is.EqualTo(TimeSpan.FromMinutes(minutes)));
        ScannerSettings scanner = settings.GetSection("Scanner").Get<ScannerSettings>() ?? new ScannerSettings();
        Assert.That(scanner.FeedSilence, Is.EqualTo(TimeSpan.FromMinutes(minutes)), "the scanner of the Worker reads the same key");
    }

    /// <summary>
    ///     Task 24.1 (б): the channel to the market-data service pings its connection every 30 seconds, also with no call
    ///     open, and drops it when no answer came in 10 seconds: a half-open connection fails by itself. Several HTTP/2
    ///     connections stay allowed, as <c>GrpcChannel</c> sets its own handler.
    /// </summary>
    [Test]
    public void The_channel_to_the_market_data_service_checks_its_connection()
    {
        using SocketsHttpHandler handler = MarketDataRegistrar.Handler();

        Assert.That(handler.KeepAlivePingDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(handler.KeepAlivePingTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(handler.KeepAlivePingPolicy, Is.EqualTo(HttpKeepAlivePingPolicy.Always));
        Assert.That(handler.EnableMultipleHttp2Connections, Is.True);
    }
    /// <summary>
    ///     Change add-backtest-history-cache, task 4.1: spec market-history-cache «Папка не задана» — without a folder of the
    ///     cache history is read from the market-data service, and the counters of the cache are zero.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    public void Without_a_folder_of_the_cache_history_comes_from_the_service(string? path)
    {
        using ServiceProvider provider = Provider(new Dictionary<string, string?> { ["MarketData:HistoryCache:Path"] = path });

        Assert.That(provider.GetRequiredService<IHistorySource>(), Is.InstanceOf<GrpcHistorySource>());
        Assert.That(provider.GetRequiredService<IHistoryCacheCounters>().Counts, Is.EqualTo(HistoryCacheCounts.Zero));
        Assert.That(MarketDataRegistrar.HistoryCache(Settings(new Dictionary<string, string?> { ["MarketData:HistoryCache:Path"] = path })), Is.Null);
    }

    /// <summary>
    ///     Task 4.1: with a folder history is read through the cache, which also gives the counters, with a limit of 40 GB by
    ///     default.
    /// </summary>
    [Test]
    public void With_a_folder_history_is_read_through_the_cache()
    {
        string folder = Path.Combine(Path.GetTempPath(), "profit-fox-tests", "history-cache", Guid.NewGuid().ToString("N"));
        using ServiceProvider provider = Provider(new Dictionary<string, string?> { ["MarketData:HistoryCache:Path"] = folder });

        Assert.That(provider.GetRequiredService<IHistorySource>(), Is.InstanceOf<CachingHistorySource>());
        Assert.That(provider.GetRequiredService<IHistoryCacheCounters>(), Is.SameAs(provider.GetRequiredService<IHistorySource>()));
        Assert.That(
            MarketDataRegistrar.HistoryCache(Settings(new Dictionary<string, string?> { ["MarketData:HistoryCache:Path"] = folder })),
            Is.EqualTo(new HistoryCacheSettings(folder, 40L * 1024 * 1024 * 1024)));
        Assert.That(Directory.Exists(folder), Is.False, "nothing is made before the first month is saved");
    }

    /// <summary>
    ///     Task 4.1: a folder that starts with <c>~/</c> is in the home folder of the user, and the limit is in gigabytes.
    /// </summary>
    [Test]
    public void A_folder_that_starts_with_a_tilde_is_in_the_home_folder()
    {
        HistoryCacheSettings? settings = MarketDataRegistrar.HistoryCache(Settings(new Dictionary<string, string?>
        {
            ["MarketData:HistoryCache:Path"] = "~/.cache/profit-fox/history",
            ["MarketData:HistoryCache:MaxSizeGb"] = "0.5"
        }));

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.That(settings, Is.EqualTo(new HistoryCacheSettings(Path.Combine(home, ".cache", "profit-fox", "history"), 512L * 1024 * 1024)));
    }

    /// <summary>
    ///     Task 4.1: a limit of the cache that is not above zero, or not a number, stops the start.
    /// </summary>
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("lots")]
    public void A_limit_of_the_cache_not_above_zero_stops_the_start(string limit)
    {
        Assert.That(
            () => Provider(new Dictionary<string, string?> { ["MarketData:HistoryCache:Path"] = "/tmp/history", ["MarketData:HistoryCache:MaxSizeGb"] = limit }).Dispose(),
            Throws.InvalidOperationException.With.Message.Contains("MarketData:HistoryCache:MaxSizeGb"));
    }

    /// <summary>
    ///     Change add-backtest-history-cache, task 5.3: the settings files of the API and of the Worker — no folder of the cache
    ///     and a limit of 40 GB in <c>appsettings.json</c>, one folder in the home folder for both processes of the Mac in
    ///     <c>appsettings.Development.json</c>.
    /// </summary>
    [TestCase("ProfitFox.Presentation")]
    [TestCase("ProfitFox.Worker")]
    public void The_settings_files_give_no_cache_by_default_and_one_folder_of_the_home_folder_on_the_Mac(string project)
    {
        string folder = Path.Combine(Root(), "backend", "src", project);
        IConfiguration production = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json")).Build();
        IConfiguration development = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(folder, "appsettings.json"))
            .AddJsonFile(Path.Combine(folder, "appsettings.Development.json"))
            .Build();

        Assert.That(production["MarketData:HistoryCache:Path"], Is.Empty);
        Assert.That(production["MarketData:HistoryCache:MaxSizeGb"], Is.EqualTo("40"));
        Assert.That(MarketDataRegistrar.HistoryCache(production), Is.Null);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.That(MarketDataRegistrar.HistoryCache(development), Is.EqualTo(new HistoryCacheSettings(Path.Combine(home, ".cache", "profit-fox", "history"), 40L * 1024 * 1024 * 1024)));
    }

    // The root of the repository: the nearest folder up from the test binaries that has .git.
    private static string Root()
    {
        var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (folder is not null && !Path.Exists(Path.Combine(folder.FullName, ".git")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The tests do not run inside the repository.");
    }

    private static IConfiguration Settings(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddConfiguration(BenchDatabase.Settings(BenchDatabase.ConnectionString))
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ServiceProvider Provider(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(Settings(values));
        return services.BuildServiceProvider();
    }
}
