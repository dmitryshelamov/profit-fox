namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The request ids of the orders of the system on T-Invest by their client ids (decision 3): written before an order goes, read back
///     to tell an order of the exchange by its <c>order_request_id</c>. An order with no row is not of the system.
/// </summary>
internal interface ITInvestOrderIds
{
    /// <summary>The request id of the order, a new random one written now unless the order has one already.</summary>
    Task<Guid> EnsureAsync(string clientId, string accountId, string instrumentUid, CancellationToken cancellationToken);

    /// <summary>The request id of the order, or <c>null</c> when the order never went to T-Invest.</summary>
    Task<Guid?> RequestOfAsync(string clientId, CancellationToken cancellationToken);

    /// <summary>The client id of the order with the request id, or <c>null</c> for an order that is not of the system.</summary>
    Task<string?> ClientIdOfAsync(Guid requestId, CancellationToken cancellationToken);

    Task SetExchangeIdAsync(string clientId, string exchangeOrderId, CancellationToken cancellationToken);
}