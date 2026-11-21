using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Infrastructure;

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
}
