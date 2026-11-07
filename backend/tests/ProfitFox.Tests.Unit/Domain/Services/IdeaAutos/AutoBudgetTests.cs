using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaAutos;

/// <summary>
///     Requirements "Бюджет автомата" and "Бюджет всех автоматов": what the open trades take of a budget, on a reference set of trades whose sums are
///     written out below.
/// </summary>
public class AutoBudgetTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Bybit = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Other = Guid.Parse("44444444-4444-4444-8444-444444444444");

    private static readonly IdeaAuto Impulse = Auto("Импульс лонг", 0);
    private static readonly IdeaAuto Falls = Auto("Падения шорт", 1);
    private static readonly IdeaAuto Gone = Auto("Старый", 2) with { DeletedAt = Day };

    // The reference set. «Импульс лонг»: real 30 waiting for its entry + real 49.98 open on another account + paper 20.02 open = 100.00; its real trade
    // closed by the stop (50), its cancelled pullback (30) and its paper trade closed by the target (40) take nothing. The open real trades of the
    // autos: 30 + 49.98 + 100 («Падения шорт») + 25 (the deleted auto) = 204.98; the manual one (300) and the paper ones are no part of it.
    private static readonly IReadOnlyList<IdeaTrade> Real =
    [
        Trade(Impulse, 30m, Bybit, IdeaTradeStatus.AwaitingEntry),
        Trade(Impulse, 49.98m, Other, IdeaTradeStatus.Open),
        Trade(Impulse, 50m, Bybit, IdeaTradeStatus.ClosedAtStop),
        Trade(Impulse, 30m, Bybit, IdeaTradeStatus.Cancelled),
        Trade(Falls, 100m, Bybit, IdeaTradeStatus.Open),
        Trade(Gone, 25m, Bybit, IdeaTradeStatus.Open),
        Trade(null, 300m, Bybit, IdeaTradeStatus.Open)
    ];

    private static readonly IReadOnlyList<PaperIdeaTrade> Paper =
    [
        PaperOf(Trade(Impulse, 20.02m, Bybit, IdeaTradeStatus.Open)),
        PaperOf(Trade(Impulse, 40m, Bybit, IdeaTradeStatus.ClosedAtTarget)),
        PaperOf(Trade(Falls, 60m, Bybit, IdeaTradeStatus.AwaitingEntry))
    ];

    [Test]
    public void The_open_real_and_paper_trades_of_the_auto_take_its_budget()
    {
        AutoBudget.Used(Impulse.Id, Books()).ShouldBe(100.00m);
        AutoBudget.Used(Falls.Id, Books()).ShouldBe(160m);
    }

    [Test]
    public void An_auto_without_open_trades_takes_nothing()
    {
        AutoBudget.Used(Auto("Новый", 3).Id, Books()).ShouldBe(0m);
    }

    [Test]
    public void The_open_real_trades_of_every_auto_take_the_budget_of_the_autos()
    {
        AutoBudget.AutosUsed(Real).ShouldBe(204.98m);
    }

    private static AutoBooks Books()
    {
        return new AutoBooks(Real, Paper, new[] { Impulse, Falls, Gone }.ToDictionary(auto => auto.Id));
    }

    private static IdeaAuto Auto(string name, int position)
    {
        var conditions = new IdeaAutoConditions([IdeaSetup.Impulse], [IdeaType.TwoR], [Interval.FiveMinutes], null, null, null, 1.2m, 0);

        return IdeaAuto.Create(Guid.NewGuid(), name, Bybit, 30m, conditions, position) with { Mode = IdeaAutoMode.Live };
    }

    private static IdeaTrade Trade(IdeaAuto? auto, decimal amount, Guid account, IdeaTradeStatus status)
    {
        var size = new IdeaTradeSize(1m, amount, 1m, 2m, 2m);
        var instrument = new Instrument(MarketCategory.Linear, $"A{Guid.NewGuid():N}USDT");

        return IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Day, instrument: instrument), account, amount, size, Day, autoId: auto?.Id) with { Status = status };
    }

    private static PaperIdeaTrade PaperOf(IdeaTrade trade)
    {
        return new PaperIdeaTrade(trade, trade.AutoId!.Value, Day);
    }
}