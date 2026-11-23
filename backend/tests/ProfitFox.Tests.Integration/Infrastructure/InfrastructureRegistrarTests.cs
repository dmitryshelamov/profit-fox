using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
}
