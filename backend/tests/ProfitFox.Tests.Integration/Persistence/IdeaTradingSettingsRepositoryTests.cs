using System.Text;

using Npgsql;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class IdeaTradingSettingsRepositoryTests
{
    private NpgsqlDataSource _dataSource = null!;
    private IdeaTradingSettingsRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _repository = new IdeaTradingSettingsRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Scenario "Настройки по умолчанию".
    [Test]
    public async Task Settings_never_saved_are_the_defaults()
    {
        IdeaTradingSettings settings = await _repository.GetAsync(CancellationToken.None);

        settings.ShouldBe(new IdeaTradingSettings(null, 20m, 200m, 5, 5m, 1.5m));
    }

    [Test]
    public async Task Saved_settings_are_read_back_and_replaced_by_the_next_save()
    {
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, DateTimeOffset.UtcNow);
        await new ExchangeAccountRepository(_dataSource).AddAsync(account, new EncryptedSecret([1], Encoding.UTF8.GetBytes("cipher"), [2]), CancellationToken.None);
        var first = new IdeaTradingSettings(account.Id, 25.5m, 300m, 3, 6m, 1.2m);

        await _repository.SaveAsync(first, CancellationToken.None);
        (await _repository.GetAsync(CancellationToken.None)).ShouldBe(first);

        await _repository.SaveAsync(first with { AccountId = null, MaxOpenTrades = 4 }, CancellationToken.None);
        (await _repository.GetAsync(CancellationToken.None)).ShouldBe(first with { AccountId = null, MaxOpenTrades = 4 });
    }

    // Task 2.6: the two guards of the autos are saved with the rest, and an empty one is saved empty.
    [Test]
    public async Task Guards_of_the_autos_are_saved_and_can_be_switched_off()
    {
        IdeaTradingSettings guarded = IdeaTradingSettings.Default with { DailyLossLimit = 30m, MaxSameDirection = 5 };

        await _repository.SaveAsync(guarded, CancellationToken.None);
        (await _repository.GetAsync(CancellationToken.None)).ShouldBe(guarded);

        await _repository.SaveAsync(guarded with { DailyLossLimit = null, MaxSameDirection = null }, CancellationToken.None);
        (await _repository.GetAsync(CancellationToken.None)).ShouldBe(IdeaTradingSettings.Default);
    }

    // The budget of all the autos is saved with the rest, and an empty one is saved empty.
    [Test]
    public async Task Budget_of_the_autos_is_saved_and_can_be_switched_off()
    {
        IdeaTradingSettings budgeted = IdeaTradingSettings.Default with { AutosBudget = 200.5m };

        await _repository.SaveAsync(budgeted, CancellationToken.None);
        (await _repository.GetAsync(CancellationToken.None)).ShouldBe(budgeted);

        await _repository.SaveAsync(IdeaTradingSettings.Default, CancellationToken.None);
        (await _repository.GetAsync(CancellationToken.None)).ShouldBe(IdeaTradingSettings.Default);
    }

    // Requirement "Настройки торговли идеями": the leverage is saved with the rest.
    [Test]
    public async Task Leverage_is_saved_with_the_settings()
    {
        IdeaTradingSettings leveraged = IdeaTradingSettings.Default with { Leverage = 3 };

        await _repository.SaveAsync(leveraged, CancellationToken.None);

        (await _repository.GetAsync(CancellationToken.None)).ShouldBe(leveraged);
    }
}