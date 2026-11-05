namespace ProfitFox.Domain.Services.Strategies.Templates;

/// <summary>A template of the catalog; a strategy made from a template does not keep it.</summary>
public enum StrategyTemplateKind
{
    EmaCrossover,
    EmaShort,
    SmaCrossover,
    Ladder,
    LadderSma,
    LadderPlus,
    Dca,
    MacdTrend,
    RsiReversion,
    BollingerReversion,
    CapitulationBuyback,
    VolumeImpulse,
    LevelTrade,
    LevelTradeShort
}