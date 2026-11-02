using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>Sends orders and cancels of an account to the exchange; a refusal is an outcome, never an exception.</summary>
public interface IOrderGateway
{
    Task<OrderOutcome> PlaceAsync(ExchangeKey key, Instrument instrument, OrderRequest order, CancellationToken cancellationToken);

    Task<OrderOutcome> CancelAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken);
}