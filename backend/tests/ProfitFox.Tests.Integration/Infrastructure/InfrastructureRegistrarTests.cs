using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Services.Ideas;
using ProfitFox.Infrastructure;
using ProfitFox.Infrastructure.Clients.MarketData;

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
}
