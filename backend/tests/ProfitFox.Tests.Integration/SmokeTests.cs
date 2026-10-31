using System.Net;

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
}
