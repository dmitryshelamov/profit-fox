using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Commands.Trades.SaveTradeSettings;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Trades;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Unit.Application.Commands.Accounts;

namespace ProfitFox.Tests.Unit.Application.Commands.Trades;

/// <summary>
///     Change take-idea-trades, task 2.2: the save and the read of the settings of the risk module (spec trade-risk
///     «Настройки сохранены», «Значения по умолчанию»).
/// </summary>
public sealed class SaveTradeSettingsCommandHandlerTests
{
    private readonly MemoryTradeSettingsRepository _settings = new MemoryTradeSettingsRepository();
    private readonly MemoryExchangeAccountRepository _accounts = new MemoryExchangeAccountRepository();
    private readonly FakeTimeProvider _time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 28, 14, 20, 0, TimeSpan.Zero).AddTicks(7));

    // Spec «Настройки сохранены».
    [Test]
    public async Task Settings_saved_are_given_at_the_next_read_with_the_moment_to_the_microsecond()
    {
        var account = new ExchangeAccount(Guid.NewGuid(), "Основной", 1, "key", _time.GetUtcNow());
        await _accounts.AddAsync(account, new ProtectedSecret([1], [2], [3]), CancellationToken.None);
        var settings = new TradeSettings { DefaultAccountId = account.Id, Leverage = 3, MaxAmount = 200m, MaxOpenTrades = 5, MaxTotalAmount = 1000m, MaxLossPercent = 3m, MaxDayLoss = 50m };

        TradeSettings saved = await new SaveTradeSettingsCommandHandler(_settings, _accounts, _time).Handle(new SaveTradeSettingsCommand(settings), CancellationToken.None);

        Assert.That(saved, Is.EqualTo(settings with { SavedAt = new DateTimeOffset(2026, 11, 28, 14, 20, 0, TimeSpan.Zero) }));
        Assert.That(await new GetTradeSettingsQueryHandler(_settings).Handle(new GetTradeSettingsQuery(), CancellationToken.None), Is.EqualTo(saved));
    }

    [Test]
    public void A_default_account_that_is_not_there_is_refused_at_its_field_and_nothing_is_saved()
    {
        var settings = new TradeSettings { DefaultAccountId = Guid.NewGuid(), Leverage = 25 };

        TradeSettingsRequestException? refused = Assert.ThrowsAsync<TradeSettingsRequestException>(() =>
            new SaveTradeSettingsCommandHandler(_settings, _accounts, _time).Handle(new SaveTradeSettingsCommand(settings), CancellationToken.None));

        Assert.That(refused!.Problems.Select(problem => problem.Field), Is.EqualTo(new[] { "defaultAccountId", "leverage" }));
        Assert.That(_settings.Saved, Is.Null);
    }

    // Spec «Значения по умолчанию»: the times and the attempts never saved read as their defaults.
    [Test]
    public async Task Before_any_save_the_settings_have_the_default_times_and_attempts()
    {
        TradeSettings read = await new GetTradeSettingsQueryHandler(_settings).Handle(new GetTradeSettingsQuery(), CancellationToken.None);
        Assert.That((read.ExpiryMargin, read.EntryWait, read.StopAttempts, read.SavedAt), Is.EqualTo((TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), 3, (DateTimeOffset?)null)));
    }

    private sealed class MemoryTradeSettingsRepository : ITradeSettingsRepository
    {
        public TradeSettings? Saved { get; private set; }

        public Task<TradeSettings> GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Saved ?? TradeSettings.Empty);
        }

        public Task SaveAsync(TradeSettings settings, CancellationToken cancellationToken)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }
}
