using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaTrades.Commands.SaveIdeaTradingSettings;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Repositories;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaTrades;

/// <summary>Requirement "Настройки торговли идеями": the leverage of the settings is a whole number from one.</summary>
public class SaveIdeaTradingSettingsTests
{
    private IIdeaTradingSettingsRepository _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = Substitute.For<IIdeaTradingSettingsRepository>();
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
    public async Task Leverage_of_one_is_saved()
    {
        await SaveAsync(IdeaTradingSettings.Default with { Leverage = 1 });

        await _settings.Received(1).SaveAsync(IdeaTradingSettings.Default, Arg.Any<CancellationToken>());
    }

    private Task SaveAsync(IdeaTradingSettings settings)
    {
        return new SaveIdeaTradingSettingsCommandHandler(_settings, Substitute.For<IExchangeAccountRepository>()).Handle(new SaveIdeaTradingSettingsCommand(settings), CancellationToken.None);
    }
}