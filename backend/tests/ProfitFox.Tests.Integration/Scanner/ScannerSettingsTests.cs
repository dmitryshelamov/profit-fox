using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

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

    private static Dictionary<string, string?> Ideas(string folder)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json")).Build();

        return configuration.GetSection("Ideas").AsEnumerable().Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value);
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