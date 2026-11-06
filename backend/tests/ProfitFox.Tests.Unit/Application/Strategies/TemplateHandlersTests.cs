using ProfitFox.Application.Strategies.Queries.GetStrategyTemplate;
using ProfitFox.Application.Strategies.Queries.GetStrategyTemplates;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Templates;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Strategies;

public class TemplateHandlersTests
{
    // Scenario "Список шаблонов".
    [Test]
    public async Task List_has_the_templates_in_the_order_of_the_catalog_with_codes_and_directions()
    {
        IReadOnlyList<StrategyTemplateSummary> templates = await new GetStrategyTemplatesQueryHandler().Handle(new GetStrategyTemplatesQuery(), CancellationToken.None);

        templates.Count.ShouldBe(14);
        templates.Select(template => template.Code).ShouldBe(
        [
            "ema-crossover", "ema-short", "sma-crossover", "ladder", "ladder-sma", "ladder-plus", "dca", "macd-trend", "rsi-reversion",
            "bollinger-reversion", "capitulation-buyback", "volume-impulse", "level-trade", "level-trade-short"
        ]);
        templates[0].ShouldBe(new StrategyTemplateSummary("ema-crossover", "EMA Crossover", BuiltInTemplates.All[0].Description, StrategyDirection.Long));
        templates[^1].ShouldBe(new StrategyTemplateSummary("level-trade-short", "Сделка по уровням (шорт)", BuiltInTemplates.All[^1].Description, StrategyDirection.Short));
    }

    // Scenario "Чтение шаблона".
    [Test]
    public async Task Template_is_read_by_its_code_with_its_definition()
    {
        StrategyTemplateResult? template = await new GetStrategyTemplateQueryHandler().Handle(new GetStrategyTemplateQuery("rsi-reversion"), CancellationToken.None);

        template!.Name.ShouldBe("Отскок RSI");
        template.Definition.ShouldBe(BuiltInTemplates.All.Single(candidate => candidate.Kind == StrategyTemplateKind.RsiReversion).Definition);
    }

    // Scenario "Неизвестный шаблон".
    [TestCase("grid")]
    [TestCase("")]
    [TestCase("EMA-CROSSOVER")]
    public async Task Unknown_code_has_no_template(string code)
    {
        (await new GetStrategyTemplateQueryHandler().Handle(new GetStrategyTemplateQuery(code), CancellationToken.None)).ShouldBeNull();
    }
}