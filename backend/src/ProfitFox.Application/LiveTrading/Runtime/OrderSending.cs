using ProfitFox.Application.Ports.LiveTrading;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>The decision both sessions, of a bot and of a trade, make on the answer of the exchange to an order they sent.</summary>
public static class OrderSending
{
    /// <param name="placing">The order was sent; otherwise its cancel was.</param>
    /// <param name="answer"></param>
    public static SendStep Resolve(bool placing, OrderOutcomeKind answer)
    {
        return answer switch
        {
            OrderOutcomeKind.Accepted when placing => SendStep.Taken,
            OrderOutcomeKind.Accepted => SendStep.CancelTaken,
            OrderOutcomeKind.AlreadyDone => SendStep.CancelTakenAndRefresh,
            OrderOutcomeKind.Transient => SendStep.Deferred,
            _ when placing => SendStep.Rejected,
            _ => SendStep.CancelRefused
        };
    }
}