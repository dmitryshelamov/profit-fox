using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.IdeaAutos.Queries.GetIdeaAutos;
using ProfitFox.Application.IdeaAutos.Queries.GetIdeaGuards;
using ProfitFox.Application.IdeaTrades.Commands.SaveIdeaTradingSettings;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.IdeaAutos;

/// <summary>
///     Requirements "Защиты автоматов в настройках", "Состояние защит" and "Итоги автомата": the budget of all the autos in the settings, and what the open
///     trades take of the budgets.
/// </summary>
public class BudgetStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 13, 20, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private IIdeaTradingSettingsRepository _settings = null!;
    private IIdeaTradeRepository _trades = null!;
    private IPaperIdeaTradeRepository _paper = null!;
    private IIdeaAutoRepository _autos = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = Substitute.For<IIdeaTradingSettingsRepository>();
        _trades = Substitute.For<IIdeaTradeRepository>();
        _paper = Substitute.For<IPaperIdeaTradeRepository>();
        _autos = Substitute.For<IIdeaAutoRepository>();
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(IdeaTradingSettings.Default with { AutosBudget = 200m });
        _trades.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _paper.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    // Scenario "Бюджет автоматов ноль".
    [TestCase(0)]
    [TestCase(-5)]
    public async Task Budget_of_the_autos_not_above_zero_is_an_error_of_its_field(decimal budget)
    {
        ValidationFailedException exception = await Should.ThrowAsync<ValidationFailedException>(() => SaveAsync(IdeaTradingSettings.Default with { AutosBudget = budget }));

        exception.Errors.Keys.ShouldBe(["autosBudget"]);
        await _settings.DidNotReceive().SaveAsync(Arg.Any<IdeaTradingSettings>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Budget_of_the_autos_is_saved_or_switched_off()
    {
        await SaveAsync(IdeaTradingSettings.Default with { AutosBudget = 200m });
        await SaveAsync(IdeaTradingSettings.Default);

        await _settings.Received(1).SaveAsync(IdeaTradingSettings.Default with { AutosBudget = 200m }, Arg.Any<CancellationToken>());
        await _settings.Received(1).SaveAsync(IdeaTradingSettings.Default, Arg.Any<CancellationToken>());
    }

    // Scenario "Занятый бюджет автоматов": real trades of the autos of 100 and 80 are open, a manual one of 50 too, and one of an auto of 30 closed.
    [Test]
    public async Task Guards_tell_what_the_open_real_trades_of_the_autos_take_of_their_budget()
    {
        var auto = Guid.NewGuid();
        _trades.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Trade(auto, 100m), Trade(Guid.NewGuid(), 80m), Trade(null, 50m), Trade(auto, 30m) with { Status = IdeaTradeStatus.ClosedAtStop }]);

        IdeaGuardsState state = await new GetIdeaGuardsQueryHandler(_settings, _trades, new FakeTimeProvider(Now)).Handle(new GetIdeaGuardsQuery(), CancellationToken.None);

        (state.AutosBudgetUsed, state.AutosBudget).ShouldBe((180m, 200m));
    }

    // Scenario "Занятый бюджет": a paper trade of 49.90 and a real one of 30.00 are open, another real one closed.
    [Test]
    public async Task List_of_the_autos_tells_what_their_open_trades_take_of_their_budgets()
    {
        IdeaAuto auto = IdeaAutoGenerator.Generate(Account, amount: 30m) with { Budget = 100m };
        IdeaAuto other = IdeaAutoGenerator.Generate(Account, "Пробои 5м", 1, amount: 30m);
        _autos.GetAllAsync(Arg.Any<CancellationToken>()).Returns([auto, other]);
        _trades.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Trade(auto.Id, 30.00m), Trade(auto.Id, 30m) with { Status = IdeaTradeStatus.ClosedAtStop }]);
        _paper.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new PaperIdeaTrade(Trade(auto.Id, 49.90m), auto.Id, Now)]);
        IExchangeAccountRepository accounts = Substitute.For<IExchangeAccountRepository>();
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new ExchangeAccount(Account, "Bybit · ideas", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Now)]);
        IIdeaAutoRefusalRepository refusals = Substitute.For<IIdeaAutoRefusalRepository>();
        refusals.CountSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());

        IReadOnlyList<IdeaAutoSummary> list = await new GetIdeaAutosQueryHandler(_autos, accounts, _paper, _trades, refusals, new FakeTimeProvider(Now)).Handle(new GetIdeaAutosQuery(), CancellationToken.None);

        list.Select(summary => summary.BudgetUsed).ShouldBe([79.90m, 0m]);
    }

    private Task SaveAsync(IdeaTradingSettings settings)
    {
        return new SaveIdeaTradingSettingsCommandHandler(_settings, Substitute.For<IExchangeAccountRepository>()).Handle(new SaveIdeaTradingSettingsCommand(settings), CancellationToken.None);
    }

    // An open trade of the amount, of an auto or manual, on a pair of its own.
    private static IdeaTrade Trade(Guid? autoId, decimal amount)
    {
        var size = new IdeaTradeSize(1m, amount, 1m, 2m, 2m);
        var instrument = new Instrument(MarketCategory.Linear, $"A{Guid.NewGuid():N}USDT");

        return IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Now, instrument: instrument), Account, amount, size, Now, autoId: autoId);
    }
}