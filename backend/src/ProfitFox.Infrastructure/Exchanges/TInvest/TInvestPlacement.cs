namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>Whether an order may go to T-Invest now (decision 6): the session takes it, the session is closed, or its price is out of the corridor.</summary>
internal enum TInvestPlacement
{
    Open,
    SessionClosed,
    OutOfCorridor
}