using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    // Task 4.2: the lane of the entries of the autos with money runs in a trading Worker only.
    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task Entries_of_the_autos_are_made_by_a_trading_worker_only(bool trading, bool lane)
    {
        using IHost host = await ProductionWorkerAsync(trading, trading ? new Dictionary<string, string?> { ["ExchangeAccounts:MasterKey"] = Convert.ToBase64String(new byte[32]) } : null);

        host.Services.GetServices<IHostedService>().Any(service => service.GetType().Name == "IdeaAutoEntryBackgroundService").ShouldBe(lane);
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

    // Scenario "Продление бота не короче аренды".
    [Test]
    public async Task Trading_worker_whose_bot_lease_would_expire_before_its_renewal_does_not_start()
    {
        Exception exception = await Should.ThrowAsync<InvalidOperationException>(() => WorkerAsync(
            "Development",
            trading: true,
            new Dictionary<string, string?>
            {
                ["Bybit:RestAddress"] = "http://127.0.0.1:5296",
                ["Bybit:SocketAddress"] = "ws://127.0.0.1:5296",
                ["Trading:LeaseDuration"] = "00:01:00",
                ["Trading:LeaseRenewal"] = "00:01:00"
            }));

        exception.Message.ShouldContain("Trading:LeaseRenewal");
    }

    // A Worker that only runs backtests keeps no bot, so the lease of bots is none of its business.
    [Test]
    public async Task Worker_without_trading_does_not_check_the_bot_lease()
    {
        using IHost host = await WorkerAsync(
            "Production",
            trading: false,
            new Dictionary<string, string?> { ["Trading:LeaseDuration"] = "00:01:00", ["Trading:LeaseRenewal"] = "00:01:00" });

        await host.StartAsync();
        await host.StopAsync();
    }

    private static Task<IHost> ProductionWorkerAsync(bool trading, IReadOnlyDictionary<string, string?>? settings = null)
    {
        return WorkerAsync("Production", trading, settings);
    }

    private static async Task<IHost> WorkerAsync(string environment, bool trading, IReadOnlyDictionary<string, string?>? settings = null)
    {
        await using Npgsql.NpgsqlDataSource database = MarketDataDatabase.Open();
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