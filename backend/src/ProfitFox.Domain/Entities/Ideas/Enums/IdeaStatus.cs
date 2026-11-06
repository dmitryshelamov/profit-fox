namespace ProfitFox.Domain.Entities.Ideas.Enums;

public enum IdeaStatus
{
    /// <summary>
    ///     «Ждёт входа»: a market idea until the open of the next minute, a pullback until a minute touches its entry, a stop entry until a
    ///     minute reaches it.
    /// </summary>
    AwaitingEntry,

    /// <summary>«Активна»: entered, neither the target nor the stop reached yet.</summary>
    Active,

    /// <summary>«Цель».</summary>
    Target,

    /// <summary>«Стоп».</summary>
    Stop,

    /// <summary>
    ///     «Истекла»: a pullback or a stop entry without an entry, a stop entry that reached its stop before the entry, or an idea that reached
    ///     neither level in its lifetime.
    /// </summary>
    Expired
}