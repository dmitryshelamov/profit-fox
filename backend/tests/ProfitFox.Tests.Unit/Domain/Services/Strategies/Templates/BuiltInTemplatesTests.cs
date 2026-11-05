using System.Text.Json.Nodes;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Templates;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Templates;

public class BuiltInTemplatesTests
{
    // Requirement "Список шаблонов": the order is fixed.
    [Test]
    public void Templates_come_in_the_order_of_the_catalog_with_their_names_and_directions()
    {
        BuiltInTemplates.All.Select(template => (template.Kind, template.Name, template.Definition.Direction)).ShouldBe(
        [
            (StrategyTemplateKind.EmaCrossover, "EMA Crossover", StrategyDirection.Long),
            (StrategyTemplateKind.EmaShort, "EMA Short", StrategyDirection.Short),
            (StrategyTemplateKind.SmaCrossover, "SMA Crossover", StrategyDirection.Long),
            (StrategyTemplateKind.Ladder, "Ladder", StrategyDirection.Long),
            (StrategyTemplateKind.LadderSma, "Ladder SMA", StrategyDirection.Long),
            (StrategyTemplateKind.LadderPlus, "Ladder+", StrategyDirection.Long),
            (StrategyTemplateKind.Dca, "DCA", StrategyDirection.Long),
            (StrategyTemplateKind.MacdTrend, "MACD по тренду", StrategyDirection.Long),
            (StrategyTemplateKind.RsiReversion, "Отскок RSI", StrategyDirection.Long),
            (StrategyTemplateKind.BollingerReversion, "Возврат от полосы Боллинджера", StrategyDirection.Long),
            (StrategyTemplateKind.CapitulationBuyback, "Откуп капитуляции", StrategyDirection.Long),
            (StrategyTemplateKind.VolumeImpulse, "Импульс на объёме", StrategyDirection.Long),
            (StrategyTemplateKind.LevelTrade, "Сделка по уровням", StrategyDirection.Long),
            (StrategyTemplateKind.LevelTradeShort, "Сделка по уровням (шорт)", StrategyDirection.Short)
        ]);
    }

    [Test]
    public void Every_template_has_a_description_of_its_idea()
    {
        BuiltInTemplates.All.ShouldAllBe(template => !string.IsNullOrWhiteSpace(template.Description));
    }

    // Requirement "Определение шаблона": one truth of behavior for the reference strategy and its template.
    [TestCase(StrategyTemplateKind.EmaShort, "ema-short")]
    [TestCase(StrategyTemplateKind.Ladder, "ladder")]
    [TestCase(StrategyTemplateKind.Dca, "dca")]
    public void Template_of_a_reference_strategy_is_its_fixture_without_layout(StrategyTemplateKind kind, string fixture)
    {
        JsonObject written = StrategyJson.WriteStrategy(BuiltInTemplates.All.Single(template => template.Kind == kind).Definition);
        JsonObject reference = StrategyJson.WriteStrategy(StrategyJson.ParseStrategy(StrategyFixtures.Element(fixture)));

        written.ToJsonString().ShouldBe(reference.ToJsonString());
    }

    private static IEnumerable<TestCaseData> Templates()
    {
        return BuiltInTemplates.All.Select(template => new TestCaseData(template).SetArgDisplayNames(template.Kind.ToString()));
    }

    // Requirement "Шаблон валиден", checked without the layout and without custom nodes.
    [TestCaseSource(nameof(Templates))]
    public void Template_passes_the_check_of_a_strategy(StrategyTemplate template)
    {
        GraphValidator.ValidateStrategy(template.Definition, BuiltInComposites.Catalog).ShouldBeEmpty();
        template.Definition.Graph.Layout.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(Templates))]
    public void Defaults_of_a_template_keep_to_the_limits_of_their_parameters(StrategyTemplate template)
    {
        template.Definition.Parameters.Select(parameter => parameter.Reject(parameter.Default)).ShouldAllBe(reason => reason == null);
        template.Definition.Parameters.Select(parameter => parameter.Name).ShouldBeUnique();
    }
}