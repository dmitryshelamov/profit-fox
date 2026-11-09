using ProfitFox.Application.Ports.LiveTrading;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>The read of the state of an order the gateway, the ledger and the streams share.</summary>
internal static class TInvestOrderReads
{
    /// <summary>The state of the order by its request id or its exchange id; <c>null</c> when T-Invest does not know it.</summary>
    /// <exception cref="ProfitFox.Application.Exceptions.ExchangeException">T-Invest refused or did not answer.</exception>
    public static async Task<OrderState?> StateAsync(TInvestCall call, ExchangeKey key, string id, OrderIdType type, CancellationToken cancellationToken)
    {
        var request = new GetOrderStateRequest { AccountId = key.ApiKey, OrderId = id, OrderIdType = type };

        try
        {
            return await call.OnceAsync(key.Secret, invoker => new OrdersService.OrdersServiceClient(invoker).GetOrderStateAsync(request), cancellationToken);
        }
        catch (TInvestNotFoundException)
        {
            return null;
        }
    }
}