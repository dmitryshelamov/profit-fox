using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Templates;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Templates;

/// <summary>Runs a template on hourly bars; the order ids are the engine's, o-1, o-2 and so on.</summary>
internal static class TemplateRuns
{
    public static readonly DateTimeOffset Midnight = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    /// <summary>The template with the defaults of its parameters but the <paramref name="values" />.</summary>
    public static StrategyRun Start(StrategyTemplateKind kind, params (string Name, decimal Value)[] values)
    {
        StrategyTemplate template = BuiltInTemplates.All.Single(candidate => candidate.Kind == kind);

        return StrategyRun.Start(
            template.Definition,
            BuiltInComposites.Catalog,
            values.ToDictionary(value => value.Name, value => (NodeValue)Values.Number(value.Value)),
            Conditions.Neutral);
    }

    /// <summary>The bar of the hour <paramref name="index" />; it closes at that hour, and its open and volume are the close and 1 unless set.</summary>
    public static IReadOnlyList<OrderCommand> Bar(this StrategyRun run, int index, decimal close, decimal? open = null, decimal volume = 1m)
    {
        decimal opened = open ?? close;

        return run.Handle(new BarClosed(At(index), new Bar(opened, Math.Max(opened, close), Math.Min(opened, close), close, volume)));
    }

    public static IReadOnlyList<OrderCommand> Fill(this StrategyRun run, int index, string order, decimal quantity, decimal price)
    {
        return run.Handle(new OrderFilled(At(index), order, quantity, price, IsComplete: true));
    }

    public static DateTimeOffset At(int index)
    {
        return Midnight.AddHours(index);
    }
}