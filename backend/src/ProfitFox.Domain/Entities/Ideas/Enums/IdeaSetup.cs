namespace ProfitFox.Domain.Entities.Ideas.Enums;

/// <summary>A pattern of a pair's movement on an interval that gives trade ideas.</summary>
public enum IdeaSetup
{
    Impulse,
    EarlyImpulse,
    Drop,
    EarlyDrop,
    HighVolatility,

    /// <summary>«Пробой»: the close beyond the edge of a zone with a margin and volume; the side of the breakout.</summary>
    Breakout,

    /// <summary>«Ложный пробой»: a sweep beyond the near edge of a zone and the close back; the side against the sweep.</summary>
    FalseBreakout,

    /// <summary>«Сжатие»: ATR / price below the 10th percentile of the pair; both sides.</summary>
    Squeeze,

    /// <summary>«Истощение»: an impulse or a drop with a climax of volume and a wick against it; the side against the move.</summary>
    Exhaustion,

    /// <summary>«Перекупленность / перепроданность»: RSI(14) back from beyond 70 or 30; the side against the zone.</summary>
    RsiExtreme,

    /// <summary>«Новый максимум / минимум»: the close beyond the high or low of the last 200 closed candles; the side of the way out.</summary>
    NewExtreme,

    /// <summary>«Объём без движения»: a candle of many medians of volume with a small body; both sides.</summary>
    QuietVolume,

    /// <summary>
    ///     «Накопление / распределение»: a squeeze whose closed candles that rose over the close before them have at least Kн times the volume of
    ///     those that fell, the price not gone that way by more than Pн ATR; long when buying dominates, short when selling does.
    /// </summary>
    Accumulation
}