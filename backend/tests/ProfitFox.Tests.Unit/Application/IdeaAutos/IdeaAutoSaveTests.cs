using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.IdeaAutos.Commands.CreateIdeaAuto;
using ProfitFox.Application.IdeaAutos.Commands.SaveIdeaAuto;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaAutos;

/// <summary>Requirements "Автомат" and "Проверка автомата": the budget of an auto on a create and a save.</summary>
public class IdeaAutoSaveTests
{
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private IIdeaAutoRepository _autos = null!;
    private IExchangeAccountRepository _accounts = null!;
    private IIdeaTradingSettingsRepository _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _autos = Substitute.For<IIdeaAutoRepository>();
        _accounts = Substitute.For<IExchangeAccountRepository>();
        _settings = Substitute.For<IIdeaTradingSettingsRepository>();
        _autos.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _accounts.GetAsync(Account, Arg.Any<CancellationToken>()).Returns(new ExchangeAccount(Account, "Bybit · ideas", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, DateTimeOffset.UnixEpoch));
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(IdeaTradingSettings.Default);
        _autos.CreateAsync(Arg.Any<IdeaAuto>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<IdeaAuto>());
    }

    // Scenario "Бюджет меньше суммы".
    [Test]
    public async Task Budget_below_the_amount_is_an_error_of_the_budget()
    {
        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => CreateAsync(Input(amount: 50m, budget: 40m)));

        exception.Errors.Keys.ShouldBe(["budget"]);
        await _autos.DidNotReceive().CreateAsync(Arg.Any<IdeaAuto>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // Scenario "Бюджет равен сумме".
    [Test]
    public async Task Budget_equal_to_the_amount_is_saved_with_the_auto()
    {
        await CreateAsync(Input(amount: 50m, budget: 50m));

        await _autos.Received(1).CreateAsync(Arg.Is<IdeaAuto>(auto => auto.Budget == 50m), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // Scenario "Автомат без бюджета".
    [Test]
    public async Task Auto_without_a_budget_is_saved_without_one()
    {
        await CreateAsync(Input(amount: 50m, budget: null));

        await _autos.Received(1).CreateAsync(Arg.Is<IdeaAuto>(auto => auto.Budget == null), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Save_writes_the_budget_and_checks_it()
    {
        IdeaAuto saved = IdeaAutoGenerator.Generate(Account, amount: 50m) with { Budget = 100m };
        _autos.GetAsync(saved.Id, Arg.Any<CancellationToken>()).Returns(saved);
        var handler = new SaveIdeaAutoCommandHandler(_autos, _accounts, _settings);

        await handler.Handle(new SaveIdeaAutoCommand(saved.Id, Input(amount: 50m, budget: 80m)), CancellationToken.None);
        await Should.ThrowAsync<ValidationFailedException>(() => handler.Handle(new SaveIdeaAutoCommand(saved.Id, Input(amount: 50m, budget: 49m)), CancellationToken.None));

        await _autos.Received(1).SaveAsync(Arg.Is<IdeaAuto>(auto => auto.Budget == 80m), Arg.Any<CancellationToken>());
    }

    private Task<Guid> CreateAsync(IdeaAutoInput input)
    {
        return new CreateIdeaAutoCommandHandler(_autos, _accounts, _settings, new FakeTimeProvider()).Handle(new CreateIdeaAutoCommand(input), CancellationToken.None);
    }

    private static IdeaAutoInput Input(decimal amount, decimal? budget)
    {
        return new IdeaAutoInput("Импульс лонг", Account, amount, ["impulse"], ["target_2r"], ["15"], null, null, null, 1.2m, 0, budget);
    }
}