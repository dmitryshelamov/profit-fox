namespace ProfitFox.Domain.Entities.Strategies.Enums;

/// <summary>How long a limit order lives on the exchange.</summary>
public enum TimeInForce
{
    /// <summary>Until it is filled or cancelled.</summary>
    GoodTilCancelled,

    /// <summary>What cannot be filled at once is cancelled.</summary>
    ImmediateOrCancel
}