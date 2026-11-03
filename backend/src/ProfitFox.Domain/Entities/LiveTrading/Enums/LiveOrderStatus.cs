namespace ProfitFox.Domain.Entities.LiveTrading.Enums;

/// <summary>Where an order of a live bot is: saved before it goes to the exchange, then on the exchange until it is done.</summary>
public enum LiveOrderStatus
{
    /// <summary>Saved, not accepted by the exchange yet.</summary>
    PendingSubmit,
    Active,

    /// <summary>Its cancel is saved, not sent yet.</summary>
    PendingCancel,

    /// <summary>The exchange took its cancel and has not confirmed it yet.</summary>
    CancelSent,
    Filled,
    Cancelled,
    Rejected
}