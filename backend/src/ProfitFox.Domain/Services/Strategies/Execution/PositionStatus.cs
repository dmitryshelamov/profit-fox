namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>Where a position is in its cycle.</summary>
public enum PositionStatus
{
    /// <summary>Holds less than the minimum quantity of an order: no entry yet, or only a carried remainder.</summary>
    Waiting,

    Open,

    /// <summary>An exit left less than the minimum quantity; the orders of the position are being cancelled.</summary>
    Closing,

    /// <summary>Has no quantity to sell and no order left; the next position is the current one.</summary>
    Closed
}