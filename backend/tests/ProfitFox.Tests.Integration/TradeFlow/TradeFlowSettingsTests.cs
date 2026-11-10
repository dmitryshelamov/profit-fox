using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.Application.TradeFlow;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

using TradeFlowProgram = ProfitFox.TradeFlow.Program;

namespace ProfitFox.Tests.Integration.TradeFlow;

/// <summary>Decision 13 of collect-trade-flow: the <c>TradeFlow</c> section of the collector.</summary>
public class TradeFlowSettingsTests
{
    // Decision 2: a holder that renews no sooner than its lease expires would lose the flow to another collector.
    [Test]
    public void Collector_whose_lease_would_expire_before_its_renewal_does_not_start()
    {
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() => Collector(
            TradeFlowHost.ContentRoot(),
            new Dictionary<string, string?> { ["TradeFlow:LeaseDuration"] = "00:00:30", ["TradeFlow:LeaseRenewal"] = "00:00:30" }));

        exception.Message.ShouldContain("TradeFlow:LeaseRenewal");
    }

    [Test]
    public void Settings_of_the_collector_are_those_of_the_design()
    {
        using IHost collector = Collector(TradeFlowHost.ContentRoot(), new Dictionary<string, string?>());

        collector.Services.GetRequiredService<TradeFlowSettings>().ShouldBeEquivalentTo(new TradeFlowSettings
        {
            HistoryDays = 30,
            ArchiveAddress = "https://public.bybit.com",
            ArchiveNotBefore = new TimeSpan(2, 30, 0),
            LeaseDuration = TimeSpan.FromSeconds(30),
            LeaseRenewal = TimeSpan.FromSeconds(10)
        });
    }

    // The file on the server, mounted into the container, overrides the settings of the image.
    [Test]
    public void Settings_file_in_the_config_folder_overrides_the_settings()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("profit-fox-trade-flow-");

        try
        {
            File.WriteAllText(Path.Combine(root.CreateSubdirectory("config").FullName, "trade-flow.json"), """{ "TradeFlow": { "HistoryDays": 7 } }""");
            using IHost collector = Collector(root.FullName, new Dictionary<string, string?> { ["TradeFlow:HistoryDays"] = "5" });

            collector.Services.GetRequiredService<TradeFlowSettings>().HistoryDays.ShouldBe(7);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static IHost Collector(string contentRoot, IReadOnlyDictionary<string, string?> settings)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Production",
            ContentRootPath = contentRoot
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = "Host=127.0.0.1;Database=unused" });
        builder.Configuration.AddInMemoryCollection(settings);
        TradeFlowProgram.Configure(builder);

        return builder.Build();
    }
}