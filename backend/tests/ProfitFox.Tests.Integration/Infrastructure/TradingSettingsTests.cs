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

    // A local run never sends real orders: a Worker in Development trades only against a stub of Bybit.
    [Test]
    public async Task Development_worker_does_not_trade_on_the_live_bybit()
    {
        Exception exception = await Should.ThrowAsync<InvalidOperationException>(() => WorkerAsync("Development", trading: true));

        exception.Message.ShouldContain("Bybit__RestAddress");
    }

    [Test]
    public async Task Development_worker_trades_against_a_stub_of_bybit()
    {
        using IHost host = await WorkerAsync(
            "Development",
            trading: true,
            new Dictionary<string, string?> { ["Bybit:RestAddress"] = "http://127.0.0.1:5296", ["Bybit:SocketAddress"] = "ws://127.0.0.1:5296" });

        await host.StartAsync();
        await host.StopAsync();
    }

    // Both addresses: orders go over REST, fills come over the streams.
    [TestCase("Bybit:RestAddress", "http://127.0.0.1:5296")]
    [TestCase("Bybit:SocketAddress", "ws://127.0.0.1:5296")]
    public async Task Development_worker_with_half_a_stub_does_not_trade(string key, string address)
    {
        await Should.ThrowAsync<InvalidOperationException>(() => WorkerAsync("Development", trading: true, new Dictionary<string, string?> { [key] = address }));
    }

    private static Task<IHost> ProductionWorkerAsync(bool trading)
    {
        return WorkerAsync("Production", trading);
    }

    private static async Task<IHost> WorkerAsync(string environment, bool trading, IReadOnlyDictionary<string, string?>? settings = null)
    {
        await using Npgsql.NpgsqlDataSource database = await MarketDataDatabase.CreateAsync();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            ContentRootPath = WorkerHost.ContentRoot()
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MarketData"] = database.ConnectionString,
            ["Trading:Enabled"] = trading ? "true" : "false"
        });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        WorkerProgram.Configure(builder);

        return builder.Build();
    }
}