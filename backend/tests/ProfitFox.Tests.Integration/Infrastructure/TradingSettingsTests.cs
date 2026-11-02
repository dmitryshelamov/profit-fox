using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using ProfitFox.Tests.Integration.Persistence;

using Shouldly;

using WorkerProgram = ProfitFox.Worker.Program;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Only a Worker that trades decrypts keys, so only it needs the master key.</summary>
public class TradingSettingsTests
{
    // Scenario "Торговля без мастер-ключа".
    [Test]
    public async Task Trading_worker_without_a_master_key_does_not_start()
    {
        using IHost host = await ProductionWorkerAsync(trading: true);

        Exception exception = await Should.ThrowAsync<Exception>(() => host.StartAsync());

        exception.ToString().ShouldContain("ExchangeAccounts__MasterKey");
    }

    // Scenario "Worker только для бэктестов".
    [Test]
    public async Task Worker_without_trading_starts_without_a_master_key()
    {
        using IHost host = await ProductionWorkerAsync(trading: false);

        await host.StartAsync();
        await host.StopAsync();
    }

    private static async Task<IHost> ProductionWorkerAsync(bool trading)
    {
        await using Npgsql.NpgsqlDataSource database = await MarketDataDatabase.CreateAsync();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Production",
            ContentRootPath = WorkerHost.ContentRoot()
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MarketData"] = database.ConnectionString,
            ["Trading:Enabled"] = trading ? "true" : "false"
        });
        WorkerProgram.Configure(builder);

        return builder.Build();
    }
}