namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>What a session does with an order after the exchange answered its send or its cancel.</summary>
public enum SendStep
{
    /// <summary>The exchange has the order.</summary>
    Taken,

    /// <summary>The exchange took the cancel and has not confirmed it yet.</summary>
    CancelTaken,

    /// <summary>The order was done already: its cancel counts as taken, and what the exchange knows of the order is read.</summary>
    CancelTakenAndRefresh,

    /// <summary>The attempt is tried again later.</summary>
    Deferred,

    /// <summary>The exchange refused the order for good: it ends rejected.</summary>
    Rejected,

    /// <summary>The exchange refused the cancel for good: the order stays, and its owner stops.</summary>
    CancelRefused
}