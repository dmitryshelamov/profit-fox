namespace ProfitFox.Domain.Entities.Ideas.Enums;

/// <summary>The rules an idea takes its entry, stop and target from.</summary>
public enum IdeaType
{
    /// <summary>«Уровни»: at the market, the stop beyond the nearest zone, the target at the next zone.</summary>
    Levels,

    /// <summary>«Цель 2R»: at the market, the stop beyond the nearest zone, the target at 2R.</summary>
    TwoR,

    /// <summary>«За экстремумом»: at the market, the stop beyond the extreme of the setup window, the target at 2R.</summary>
    BeyondExtreme,

    /// <summary>«Откат»: a limit at the middle of the move of the setup window, the stop beyond its extreme, the target at 2R.</summary>
    Pullback,

    /// <summary>«Стоп-вход»: a stop order beyond the edge of the setup range, the stop beyond the other edge, the target at 2R.</summary>
    StopEntry
}