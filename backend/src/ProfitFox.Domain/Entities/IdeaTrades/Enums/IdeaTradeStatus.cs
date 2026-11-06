namespace ProfitFox.Domain.Entities.IdeaTrades.Enums;

/// <summary>Where a trade by an idea is.</summary>
public enum IdeaTradeStatus
{
    /// <summary>«Ждёт входа»: the entry is saved or on the exchange, nothing of it is filled.</summary>
    AwaitingEntry,

    /// <summary>«Открыта»: the entry is filled, wholly or in part, and the stop and the target stand on the exchange.</summary>
    Open,

    /// <summary>«Закрыта по цели».</summary>
    ClosedAtTarget,

    /// <summary>«Закрыта по стопу».</summary>
    ClosedAtStop,

    /// <summary>«Закрыта вручную»: by the market close of the owner.</summary>
    ClosedManually,

    /// <summary>«Отменена»: the entry ended with no fill.</summary>
    Cancelled,

    /// <summary>«Ошибка»: the exchange refused the entry, or the books differ from the exchange. The reason stays with it.</summary>
    Failed
}