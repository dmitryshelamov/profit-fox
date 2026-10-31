using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration;

public class SmokeTests
{
    [Test]
    public async Task App_starts_against_a_fresh_timescale_database()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/no-such-route");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Development settings point at the local development database and load user secrets; tests must not read them.
    [Test]
    public async Task App_under_test_runs_in_the_testing_environment()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync();

        factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName.ShouldBe("Testing");
    }
}