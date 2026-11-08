using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.LiveTrading;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Decision 1: the trading on MOEX is the setting <c>TInvest:Trading</c> of the API, off without it.</summary>
public class MoexTradingSettingsTests
{
    [Test]
    public async Task Trading_on_moex_is_off_without_the_setting()
    {
        await using var factory = ProfitFoxFactory.Create(workers: 0);
        factory.CreateClient().Dispose();

        factory.Services.GetRequiredService<MoexTradingSettings>().Enabled.ShouldBeFalse();
    }

    [Test]
    public async Task Trading_on_moex_is_on_with_the_setting()
    {
        await using var factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString)
        {
            Workers = 0,
            ApiSettings = new Dictionary<string, string?> { ["TInvest:Trading"] = "true" }
        };
        factory.CreateClient().Dispose();

        factory.Services.GetRequiredService<MoexTradingSettings>().Enabled.ShouldBeTrue();
    }
}