using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The private stream of an account on <c>BybitSocketClient</c> of <c>Bybit.Net</c> (change take-idea-trades, design.md,
///     decision 3): the topics <c>order</c>, <c>execution</c> and <c>position</c> over one connection; the ping, the reconnect
///     and the new authorization are the package's. The client lives as long as the subscription and holds the decrypted
///     secret all that time. Only the linear category is passed on. The package gets no journal: its records could carry the
///     request of the authorization.
/// </summary>
internal sealed class BybitAccountStream(BybitSettings settings, ILogger<BybitAccountStream>? logger = null) : IExchangeStream
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<IExchangeSubscription> OpenAsync(
        ExchangeCredentials credentials,
        Action<ExchangeMessage> onMessage,
        Action<ExchangeStreamState> onState,
        CancellationToken cancellationToken)
    {
        var client = new BybitSocketClient(
            Options.Create(new BybitSocketOptions { ApiCredentials = new BybitCredentials(credentials.ApiKey, credentials.Secret), Environment = settings.Environment() }),
            NullLoggerFactory.Instance);
        var subscription = new BybitStreamSubscription(client, onState);
        try
        {
            subscription.Add(Confirmed("order", await client.V5PrivateApi.SubscribeToOrderUpdatesAsync(update => Pass(update.Data, onMessage), cancellationToken)));
            subscription.Add(Confirmed("execution", await client.V5PrivateApi.SubscribeToUserTradeUpdatesAsync(update => Pass(update.Data, onMessage), cancellationToken)));
            subscription.Add(Confirmed("position", await client.V5PrivateApi.SubscribeToPositionUpdatesAsync(update => Pass(update.Data, onMessage), cancellationToken)));
            return subscription;
        }
        catch
        {
            await subscription.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    ///     The messages of the port of the updates of the topic <c>order</c>, of the linear category only.
    /// </summary>
    public static IEnumerable<ExchangeMessage> Messages(IEnumerable<BybitOrderUpdate> updates)
    {
        return updates.Where(update => update.Category == global::Bybit.Net.Enums.Category.Linear).Select(BybitMessages.Of);
    }

    /// <summary>
    ///     The messages of the port of the updates of the topic <c>execution</c>, of the linear category only.
    /// </summary>
    public static IEnumerable<ExchangeMessage> Messages(IEnumerable<BybitUserTradeUpdate> updates)
    {
        return updates.Where(update => update.Category == global::Bybit.Net.Enums.Category.Linear).Select(BybitMessages.Of);
    }

    /// <summary>
    ///     The messages of the port of the updates of the topic <c>position</c>, of the linear category only.
    /// </summary>
    public static IEnumerable<ExchangeMessage> Messages(IEnumerable<BybitPositionUpdate> updates)
    {
        return updates.Where(update => update.Category == global::Bybit.Net.Enums.Category.Linear).Select(BybitMessages.Of);
    }

    private void Pass(BybitOrderUpdate[] updates, Action<ExchangeMessage> onMessage)
    {
        Each(Messages(updates), onMessage);
    }

    private void Pass(BybitUserTradeUpdate[] updates, Action<ExchangeMessage> onMessage)
    {
        Each(Messages(updates), onMessage);
    }

    private void Pass(BybitPositionUpdate[] updates, Action<ExchangeMessage> onMessage)
    {
        Each(Messages(updates), onMessage);
    }

    private void Each(IEnumerable<ExchangeMessage> messages, Action<ExchangeMessage> onMessage)
    {
        foreach (ExchangeMessage message in messages)
        {
            try
            {
                onMessage(message);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A message of the stream of Bybit could not be taken: {Kind} of {Symbol}", message.GetType().Name, message.Symbol);
            }
        }
    }

    private UpdateSubscription Confirmed(string topic, WebSocketResult<UpdateSubscription> result)
    {
        if (result.Success)
        {
            return result.Data!;
        }

        _logger.LogWarning("Bybit refused the subscription {Topic}: {Code}, {Reason}", topic, result.Error?.ErrorCode, result.Error?.Message);
        throw new ExchangeException($"Bybit не открыл поток {topic}: {result.Error?.Message}");
    }
}
