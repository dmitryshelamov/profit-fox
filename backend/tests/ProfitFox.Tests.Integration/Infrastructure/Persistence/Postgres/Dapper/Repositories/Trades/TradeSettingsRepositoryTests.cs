using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     Change take-idea-trades, task 2.2: the settings of the risk module in <c>trade_settings</c> (spec trade-risk «Настройки
///     сохранены», «Значения по умолчанию»).
/// </summary>
[NonParallelizable]
public sealed class TradeSettingsRepositoryTests
{
    private ITradeSettingsRepository _settings = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _settings = BenchServices.Get<ITradeSettingsRepository>();
    }

    [Test]
    public async Task Before_the_first_save_there_are_no_limits_and_the_default_times_and_attempts()
    {
        Assert.That(await _settings.GetAsync(CancellationToken.None), Is.EqualTo(TradeSettings.Empty));
    }

    // Spec «Настройки сохранены».
    [Test]
    public async Task Saved_settings_read_back_as_they_were()
    {
        ExchangeAccount account = await TradeStore.Account();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var saved = new TradeSettings
        {
            DefaultAccountId = account.Id,
            DefaultAmount = 100m,
            Leverage = 3,
            MaxAmount = 200m,
            MaxOpenTrades = 5,
            MaxTotalAmount = 1000m,
            MaxLossPercent = 3m,
            MaxDayLoss = 50m,
            ExpiryMargin = TimeSpan.FromMinutes(5),
            EntryWait = TimeSpan.FromSeconds(45),
            StopAttempts = 4,
            SavedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond))
        };

        await _settings.SaveAsync(saved, CancellationToken.None);
        await _settings.SaveAsync(saved, CancellationToken.None);

        Assert.That(await _settings.GetAsync(CancellationToken.None), Is.EqualTo(saved));
    }

    [Test]
    public async Task The_default_account_removed_leaves_the_setting_empty()
    {
        ExchangeAccount account = await TradeStore.Account();
        await _settings.SaveAsync(new TradeSettings { DefaultAccountId = account.Id, SavedAt = DateTimeOffset.UtcNow }, CancellationToken.None);

        await BenchServices.Get<IExchangeAccountRepository>().DeleteAsync(account.Id, CancellationToken.None);

        Assert.That((await _settings.GetAsync(CancellationToken.None)).DefaultAccountId, Is.Null);
    }
}
