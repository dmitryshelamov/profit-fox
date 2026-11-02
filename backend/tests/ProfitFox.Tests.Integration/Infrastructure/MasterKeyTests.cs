using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

public class MasterKeyTests
{
    // Scenario "Прод без мастер-ключа".
    [Test]
    public async Task Production_without_a_master_key_does_not_start()
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Environment = "Production", MasterKey = null, Workers = 0 };

        Exception exception = Should.Throw<Exception>(() => factory.CreateClient());

        exception.ToString().ShouldContain("ExchangeAccounts__MasterKey");
    }

    [Test]
    public async Task A_master_key_of_the_wrong_length_does_not_start_and_is_not_shown()
    {
        string shortKey = Convert.ToBase64String(Enumerable.Range(1, 16).Select(value => (byte)value).ToArray());
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Environment = "Production", MasterKey = shortKey, Workers = 0 };

        Exception exception = Should.Throw<Exception>(() => factory.CreateClient());

        exception.ToString().ShouldContain("ExchangeAccounts__MasterKey");
        exception.ToString().ShouldNotContain(shortKey);
    }

    // Scenario "Разработка без переопределения".
    [Test]
    public async Task Development_starts_with_the_development_master_key()
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Environment = "Development", MasterKey = null, Workers = 0 };

        AssertEncrypts(factory);
    }

    [Test]
    public async Task Testing_starts_with_its_own_master_key()
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { MasterKey = null, Workers = 0 };

        AssertEncrypts(factory);
    }

    private static void AssertEncrypts(ProfitFoxFactory factory)
    {
        using HttpClient client = factory.CreateClient();
        ISecretProtector protector = factory.Services.GetRequiredService<ISecretProtector>();
        var id = Guid.NewGuid();

        EncryptedSecret encrypted = protector.Encrypt(id, "secret");

        protector.Decrypt(id, encrypted).ShouldBe("secret");
    }
}