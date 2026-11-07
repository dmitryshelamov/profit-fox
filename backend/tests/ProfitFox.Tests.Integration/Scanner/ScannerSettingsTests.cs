using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;

using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Integration.Persistence;

using Shouldly;

using ScannerProgram = ProfitFox.Scanner.Program;

namespace ProfitFox.Tests.Integration.Scanner;

public class ScannerSettingsTests
{
    // Decision 2: a holder that renews no sooner than its lease expires would lose the stream to another scanner.
    [Test]
    public async Task Scanner_whose_lease_would_expire_before_its_renewal_does_not_start()
    {
        Exception exception = await Should.ThrowAsync<InvalidOperationException>(() => ScannerAsync(
            new Dictionary<string, string?> { ["Scanner:LeaseDuration"] = "00:00:30", ["Scanner:LeaseRenewal"] = "00:00:30" }));

        exception.Message.ShouldContain("Scanner:LeaseRenewal");
    }

    // Decision 8: the API measures the own idea and the zones by the same k and rules of the zones as the scanner.
    [Test]
    public void Api_and_scanner_hold_the_same_ideas_settings()
    {
        string root = Path.GetDirectoryName(ScannerHost.ContentRoot())!;

        Ideas(Path.Combine(root, "ProfitFox.Presentation")).ShouldBe(Ideas(ScannerHost.ContentRoot()));
        Ideas(ScannerHost.ContentRoot()).ShouldContainKey("Ideas:StopBuffer");
    }

    // Requirement "Проскальзывание идей": a service that lost the slippage of a category would count the ideas without it, so it does not start.
    [Test]
    public async Task Scanner_without_the_slippage_of_a_category_does_not_start()
    {
        using IHost scanner = Scanner(ScannerHost.ContentRoot(), new Dictionary<string, string?> { ["Backtests:DefaultFees:spot:Slippage"] = "" });

        OptionsValidationException exception = await Should.ThrowAsync<OptionsValidationException>(() => scanner.StartAsync());

        exception.Message.ShouldContain("Backtests:DefaultFees:spot:Slippage");
    }

    // Decision 2 of add-idea-slippage: the API, the scanner and the Worker count the ideas by the same fees and slippage.
    [Test]
    public void Api_scanner_and_worker_hold_the_same_fees_and_slippage()
    {
        string root = Path.GetDirectoryName(ScannerHost.ContentRoot())!;

        Fees(Path.Combine(root, "ProfitFox.Presentation")).ShouldBe(Fees(ScannerHost.ContentRoot()));
        Fees(Path.Combine(root, "ProfitFox.Worker")).ShouldBe(Fees(ScannerHost.ContentRoot()));
        Fees(ScannerHost.ContentRoot()).ShouldContainKeyAndValue("Backtests:DefaultFees:linear:Slippage", "0.05");
        Fees(ScannerHost.ContentRoot()).ShouldContainKeyAndValue("Backtests:DefaultFees:spot:Slippage", "0.1");
    }

    // Decision 9: the settings file carries the provisional thresholds of the candle setups.
    [Test]
    public void Scanner_settings_carry_the_thresholds_of_the_candle_setups()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(ScannerHost.ContentRoot(), "appsettings.json")).Build();

        IdeasSettings settings = configuration.GetSection("Ideas").Get<IdeasSettings>()!;

        settings.Thresholds.ShouldBeEquivalentTo(new ThresholdSettings());
        Ideas(ScannerHost.ContentRoot()).ShouldContainKey("Ideas:Thresholds:BreakoutMargin");
        Ideas(ScannerHost.ContentRoot()).ShouldContainKeyAndValue("Ideas:Thresholds:BiasRatio", "1.5");
        Ideas(ScannerHost.ContentRoot()).ShouldContainKeyAndValue("Ideas:Thresholds:FlatMove", "0.5");
    }

    private static Dictionary<string, string?> Fees(string folder)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json")).Build();

        return configuration.GetSection("Backtests:DefaultFees").AsEnumerable().Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    private static Dictionary<string, string?> Ideas(string folder)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json")).Build();

        return configuration.GetSection("Ideas").AsEnumerable().Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    // The file on the server, mounted into the container, changes the settings without a restart; without it the scanner starts as before.
    [Test]
    public async Task Settings_file_in_the_config_folder_changes_the_settings_while_the_scanner_runs()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("profit-fox-scanner-");

        try
        {
            string file = Path.Combine(root.CreateSubdirectory("config").FullName, "scanner.json");
            await File.WriteAllTextAsync(file, """{ "Scanner": { "RestRequestsPerSecond": 7 } }""");
            using IHost scanner = Scanner(root.FullName, new Dictionary<string, string?> { ["Scanner:RestRequestsPerSecond"] = "5" });
            IOptionsMonitor<ScannerSettings> settings = scanner.Services.GetRequiredService<IOptionsMonitor<ScannerSettings>>();
            int before = settings.CurrentValue.RestRequestsPerSecond;
            var changed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable? listener = settings.OnChange(current => changed.TrySetResult(current.RestRequestsPerSecond));

            await File.WriteAllTextAsync(file, """{ "Scanner": { "RestRequestsPerSecond": 9 } }""");

            before.ShouldBe(7);
            (await changed.Task.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(9);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static IHost Scanner(string contentRoot, IReadOnlyDictionary<string, string?> settings)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Production",
            ContentRootPath = contentRoot
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = "Host=127.0.0.1;Database=unused" });
        builder.Configuration.AddInMemoryCollection(settings);
        ScannerProgram.Configure(builder);

        return builder.Build();
    }

    private static async Task<IHost> ScannerAsync(IReadOnlyDictionary<string, string?> settings)
    {
        await using Npgsql.NpgsqlDataSource database = MarketDataDatabase.Open();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Production",
            ContentRootPath = ScannerHost.ContentRoot()
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = database.ConnectionString });
        builder.Configuration.AddInMemoryCollection(settings);
        ScannerProgram.Configure(builder);

        return builder.Build();
    }
}