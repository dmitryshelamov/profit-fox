using System.Net;
using System.Text.Json.Nodes;

using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Integration.Worker;

namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     Change add-exchange-accounts, task 2.2 (design.md, decisions 1 and 2): spec exchange-accounts «Мастер-ключ
///     обязателен» — the API does not start without the master key or with one of another kind, and the message names the
///     setting without the value; a Worker starts without it; the development key is only in the settings of development.
/// </summary>
[NonParallelizable]
public sealed class MasterKeyTests
{
    private MarketDataServer _market = null!;

    [SetUp]
    public async Task Start()
    {
        _market = await MarketDataServer.Start();
    }

    [TearDown]
    public async Task Stop()
    {
        await _market.DisposeAsync();
    }

    // Spec «Ключа нет».
    [Test]
    public async Task Without_the_master_key_the_API_does_not_start_and_names_the_setting()
    {
        await using var api = new BenchTestApplication(_market, masterKey: "");

        Exception? refused = Assert.Catch(() => _ = api.Http);

        Assert.That(refused!.Message, Does.Contain("ExchangeAccounts:MasterKey"));
    }

    // Spec «Ключ неверной длины»: 16 bytes in base64, and text that is not base64.
    [TestCase("MDEyMzQ1Njc4OWFiY2RlZg==")]
    [TestCase("not-a-key-at-all-but-long-enough-to-look-like-one!")]
    public async Task With_a_master_key_of_another_kind_the_API_does_not_start_and_does_not_tell_the_value(string key)
    {
        await using var api = new BenchTestApplication(_market, masterKey: key);

        Exception? refused = Assert.Catch(() => _ = api.Http);

        Assert.That(refused!.Message, Does.Contain("ExchangeAccounts:MasterKey"));
        Assert.That(refused.ToString(), Does.Not.Contain(key));
    }

    [Test]
    public async Task With_the_master_key_the_API_starts()
    {
        await using var api = new BenchTestApplication(_market);

        Assert.That((await api.Http.GetAsync("/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_Worker_starts_without_the_master_key()
    {
        await using WorkerHost worker = await WorkerHost.Start(_market, "no-master-key", runs: false);

        Assert.That(worker.Name, Is.EqualTo("no-master-key"));
    }

    [Test]
    public void The_development_key_is_only_in_the_settings_of_development()
    {
        string presentation = Path.Combine(Root(), "src", "ProfitFox.Presentation");

        JsonNode shared = JsonNode.Parse(File.ReadAllText(Path.Combine(presentation, "appsettings.json")))!;
        JsonNode development = JsonNode.Parse(File.ReadAllText(Path.Combine(presentation, "appsettings.Development.json")))!;
        JsonNode worker = JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "src", "ProfitFox.Worker", "appsettings.json")))!;
        JsonNode workerDevelopment = JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "src", "ProfitFox.Worker", "appsettings.Development.json")))!;

        Assert.That(shared["ExchangeAccounts"], Is.Null);
        Assert.That(worker["ExchangeAccounts"], Is.Null);
        Assert.That(workerDevelopment["ExchangeAccounts"], Is.Null);
        Assert.That(Convert.FromBase64String(development["ExchangeAccounts"]!["MasterKey"]!.GetValue<string>()), Has.Length.EqualTo(32));
    }

    // The folder backend of the repository, above the folder of the test assembly.
    private static string Root()
    {
        var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(folder.FullName, "ProfitFox.slnx")))
        {
            folder = folder.Parent ?? throw new DirectoryNotFoundException("ProfitFox.slnx is not above the tests.");
        }

        return folder.FullName;
    }
}
