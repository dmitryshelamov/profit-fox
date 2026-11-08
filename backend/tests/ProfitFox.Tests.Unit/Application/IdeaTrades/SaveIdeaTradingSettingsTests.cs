using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaTrades.Commands.SaveIdeaTradingSettings;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaTrades;

/// <summary>
///     Requirement "Настройки торговли по идеям": the account of the settings is one the trades by ideas can go through, and the leverage of the
///     settings is a whole number from one.
/// </summary>
public class SaveIdeaTradingSettingsTests
{
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private IIdeaTradingSettingsRepository _settings = null!;
    private IExchangeAccountRepository _accounts = null!;
    private SaveIdeaTradingSettingsCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = Substitute.For<IIdeaTradingSettingsRepository>();
        _accounts = Substitute.For<IExchangeAccountRepository>();
        _handler = new SaveIdeaTradingSettingsCommandHandler(_settings, _accounts);
    }

    [Test]
    public async Task Bybit_account_is_saved()
    {
        _accounts.GetAsync(Account, Arg.Any<CancellationToken>()).Returns(new ExchangeAccount(Account, "Bybit · ideas", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, DateTimeOffset.UnixEpoch));

        await _handler.Handle(new SaveIdeaTradingSettingsCommand(IdeaTradingSettings.Default with { AccountId = Account }), CancellationToken.None);

        await _settings.Received(1).SaveAsync(Arg.Is<IdeaTradingSettings>(saved => saved!.AccountId == Account), Arg.Any<CancellationToken>());
    }

    // After the review: the trades by ideas go through Bybit only; an account of T-Invest is an error of the account, not a null key at the entry.
    [Test]
    public async Task TInvest_account_is_an_error_of_the_account()
    {
        _accounts.GetAsync(Account, Arg.Any<CancellationToken>())
            .Returns(new ExchangeAccount(Account, "Брокерский", Exchange.TInvest, null, null, "2000000001", new HashSet<MarketCategory> { MarketCategory.Moex }, DateTimeOffset.UnixEpoch));

        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => _handler.Handle(new SaveIdeaTradingSettingsCommand(IdeaTradingSettings.Default with { AccountId = Account }), CancellationToken.None));

        exception.Errors.Keys.ShouldBe(["accountId"]);
    }

    // Scenario "Плечо ноль".
    [Test]
    public async Task Leverage_below_one_is_an_error_of_its_field_and_nothing_is_saved()
    {
        ValidationFailedException error = await Should.ThrowAsync<ValidationFailedException>(() => SaveAsync(IdeaTradingSettings.Default with { Leverage = 0 }));

        error.Errors.Keys.ShouldBe(["leverage"]);
        await _settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Test]
    public async Task Unknown_account_is_an_error_of_the_account()
    {
        _accounts.GetAsync(Account, Arg.Any<CancellationToken>()).Returns((ExchangeAccount?)null);

        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => _handler.Handle(new SaveIdeaTradingSettingsCommand(IdeaTradingSettings.Default with { AccountId = Account }), CancellationToken.None));

        exception.Errors.Keys.ShouldBe(["accountId"]);
    }

    [Test]
    public async Task Leverage_of_one_is_saved()
    {
        await SaveAsync(IdeaTradingSettings.Default with { Leverage = 1 });

        await _settings.Received(1).SaveAsync(IdeaTradingSettings.Default, Arg.Any<CancellationToken>());
    }

    private Task SaveAsync(IdeaTradingSettings settings)
    {
        return _handler.Handle(new SaveIdeaTradingSettingsCommand(settings), CancellationToken.None);
    }
}