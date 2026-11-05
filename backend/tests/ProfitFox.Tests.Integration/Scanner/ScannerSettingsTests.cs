using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Scanner;

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
        await using Npgsql.NpgsqlDataSource database = await MarketDataDatabase.CreateAsync();
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