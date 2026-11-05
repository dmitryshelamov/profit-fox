namespace ProfitFox.Domain.Services.Strategies.Templates;

/// <summary>Templates shipped with the code, in the order the owner sees them.</summary>
public static class BuiltInTemplates
{
    public static IReadOnlyList<StrategyTemplate> All { get; } =
    [
        new StrategyTemplate(StrategyTemplateKind.EmaCrossover, "EMA Crossover", "Покупка, когда быстрая EMA пересекает медленную вверх; выход на пересечении вниз или по стопу от цены входа.", IndicatorTemplates.EmaCrossover()),
        new StrategyTemplate(StrategyTemplateKind.EmaShort, "EMA Short", "Продажа в шорт, когда быстрая EMA пересекает медленную вниз; выход на пересечении вверх.", ReferenceTemplates.EmaShort()),
        new StrategyTemplate(StrategyTemplateKind.SmaCrossover, "SMA Crossover", "Покупка, когда быстрая SMA пересекает медленную вверх; выход на пересечении вниз.", IndicatorTemplates.SmaCrossover()),
        new StrategyTemplate(StrategyTemplateKind.Ladder, "Ladder", "Покупка на старте, лимитная продажа выше средней цены и докупка лимитом ниже последнего входа в пределах депозита.", ReferenceTemplates.Ladder()),
        new StrategyTemplate(StrategyTemplateKind.LadderSma, "Ladder SMA", "Лесенка, у которой докупка стоит ниже SMA закрытий, а не ниже последнего входа.", ReferenceTemplates.LadderSma()),
        new StrategyTemplate(StrategyTemplateKind.LadderPlus, "Ladder+", "Лесенка со стопом от средней цены, которая не начинает новую позицию после цели по прибыли закрытых позиций.", ReferenceTemplates.LadderPlus()),
        new StrategyTemplate(StrategyTemplateKind.Dca, "DCA", "Покупка на фиксированную сумму через равные промежутки времени, пока не исчерпан лимит депозита.", ReferenceTemplates.Dca()),
        new StrategyTemplate(StrategyTemplateKind.MacdTrend, "MACD по тренду", "Покупка, когда линия MACD пересекает сигнальную вверх выше EMA тренда; выход на пересечении вниз или по стопу.", IndicatorTemplates.MacdTrend()),
        new StrategyTemplate(StrategyTemplateKind.RsiRebound, "Отскок RSI", "Покупка, когда RSI поднимается выше уровня входа; выход при достижении уровня выхода или по стопу.", IndicatorTemplates.RsiRebound()),
        new StrategyTemplate(StrategyTemplateKind.BollingerReturn, "Возврат от полосы Боллинджера", "Покупка, когда цена закрывается ниже нижней полосы; выход на возврате к средней полосе или по стопу.", IndicatorTemplates.BollingerReturn()),
        new StrategyTemplate(StrategyTemplateKind.CapitulationBuyback, "Откуп капитуляции", "Покупка резкого падения на всплеске объёма; выход по цели прибыли или через заданное число баров.", IndicatorTemplates.CapitulationBuyback()),
        new StrategyTemplate(StrategyTemplateKind.VolumeImpulse, "Импульс на объёме", "Покупка резкого роста на всплеске объёма; выход по цели прибыли, по стопу или через заданное число баров.", IndicatorTemplates.VolumeImpulse()),
        new StrategyTemplate(StrategyTemplateKind.LevelTrade, "Сделка по уровням", "Одна сделка: лимитная покупка по цене входа, лимитная продажа по цели и рыночный выход по стопу.", LevelTemplates.LevelTrade()),
        new StrategyTemplate(StrategyTemplateKind.LevelTradeShort, "Сделка по уровням (шорт)", "Одна сделка в шорт: лимитная продажа по цене входа, лимитная покупка по цели и рыночный выход по стопу.", LevelTemplates.LevelTradeShort())
    ];
}