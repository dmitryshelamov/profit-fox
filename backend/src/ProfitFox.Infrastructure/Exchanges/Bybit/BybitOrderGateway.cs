using System.Net;

using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

using OrderSide = ProfitFox.Domain.Entities.Strategies.Enums.OrderSide;
using OrderType = ProfitFox.Domain.Entities.Strategies.Enums.OrderType;
using TimeInForce = ProfitFox.Domain.Entities.Strategies.Enums.TimeInForce;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>Sends orders and cancels with the key of an account, one attempt per call; what Bybit refuses is told by its code.</summary>
internal sealed class BybitOrderGateway(HttpClient http, ILoggerFactory loggers, BybitSettings settings) : IOrderGateway
{
    // An order link id Bybit has taken already, in linear and on the spot: the order is there.
    private static readonly HashSet<string> RepeatedClientIdCodes = ["110072", "170141"];

    // Bybit's own failures: an internal error, a timeout of its server, the rate limit.
    private static readonly HashSet<string> TransientCodes = ["10000", "10006", "10016"];

    // A cancel of an order that does not exist or is done, in linear and on the spot.
    private static readonly HashSet<string> FinishedOrderCodes = ["110001", "170213"];

    public async Task<OrderOutcome> PlaceAsync(ExchangeKey key, Instrument instrument, OrderRequest order, CancellationToken cancellationToken)
    {
        using BybitRestClient client = BybitKeyClients.Create(http, loggers, settings, key.ApiKey, key.Secret);
        bool byAmount = order.QuoteAmount is not null && instrument.Category == MarketCategory.Spot;
        // Bybit sizes a spot market buy in the quote coin by default: the unit is always said.
        MarketUnit? unit = instrument.Category == MarketCategory.Spot && order.Type == OrderType.Market
            ? byAmount ? MarketUnit.QuoteAsset : MarketUnit.BaseAsset
            : null;
        // The stop and the target of an entry: on linear for the quantity of this order only (Partial), to the market when the last price
        // reaches them; on the spot Bybit takes them on a limit order and knows no mode.
        Protection? protection = order.Protection;
        bool linear = instrument.Category == MarketCategory.Linear;
        HttpResult<BybitOrderId> result = await client.V5Api.Trading.PlaceOrderAsync(
            instrument.Category.ToBybit(),
            instrument.Symbol,
            order.Side == OrderSide.Buy ? global::Bybit.Net.Enums.OrderSide.Buy : global::Bybit.Net.Enums.OrderSide.Sell,
            order.Type == OrderType.Limit ? NewOrderType.Limit : NewOrderType.Market,
            byAmount ? order.QuoteAmount!.Value : order.Quantity!.Value,
            order.Price,
            timeInForce: order.Type == OrderType.Limit ? order.TimeInForce == TimeInForce.ImmediateOrCancel ? global::Bybit.Net.Enums.TimeInForce.ImmediateOrCancel : global::Bybit.Net.Enums.TimeInForce.GoodTillCanceled : null,
            positionIdx: protection is not null && linear ? PositionIdx.OneWayMode : null,
            clientOrderId: order.ClientId,
            takeProfitOrderType: protection is not null && linear ? global::Bybit.Net.Enums.OrderType.Market : null,
            takeProfit: protection?.TakeProfit,
            stopLossOrderType: protection is not null && linear ? global::Bybit.Net.Enums.OrderType.Market : null,
            stopLoss: protection?.StopLoss,
            takeProfitTriggerBy: protection is not null && linear ? TriggerType.LastPrice : null,
            stopLossTriggerBy: protection is not null && linear ? TriggerType.LastPrice : null,
            reduceOnly: order.ReduceOnly ? true : null,
            stopLossTakeProfitMode: protection is not null && linear ? StopLossTakeProfitMode.Partial : null,
            marketUnit: unit,
            ct: cancellationToken);

        return result.Success ? new OrderOutcome(OrderOutcomeKind.Accepted, result.Data.OrderId) : Refusal(result, RepeatedClientIdCodes, OrderOutcomeKind.Accepted);
    }

    public async Task<OrderOutcome> CancelAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        using BybitRestClient client = BybitKeyClients.Create(http, loggers, settings, key.ApiKey, key.Secret);
        HttpResult<BybitOrderId> result = await client.V5Api.Trading.CancelOrderAsync(instrument.Category.ToBybit(), instrument.Symbol, clientOrderId: clientId, ct: cancellationToken);

        return result.Success ? new OrderOutcome(OrderOutcomeKind.Accepted, result.Data.OrderId) : Refusal(result, FinishedOrderCodes, OrderOutcomeKind.AlreadyDone);
    }

    public async Task<OrderOutcome> CancelByExchangeIdAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken)
    {
        using BybitRestClient client = BybitKeyClients.Create(http, loggers, settings, key.ApiKey, key.Secret);
        HttpResult<BybitOrderId> result = await client.V5Api.Trading.CancelOrderAsync(instrument.Category.ToBybit(), instrument.Symbol, orderId: exchangeOrderId, ct: cancellationToken);

        return result.Success ? new OrderOutcome(OrderOutcomeKind.Accepted, result.Data.OrderId) : Refusal(result, FinishedOrderCodes, OrderOutcomeKind.AlreadyDone);
    }

    /// <param name="result"></param>
    /// <param name="known">Codes that are no refusal for this call.</param>
    /// <param name="knownKind">What they mean.</param>
    private static OrderOutcome Refusal<T>(HttpResult<T> result, HashSet<string> known, OrderOutcomeKind knownKind)
    {
        string? code = result.Error!.ErrorCode;

        if (code is not null && known.Contains(code))
        {
            return new OrderOutcome(knownKind);
        }

        // No answer at all (connection, timeout), a 5xx, the rate limit (HTTP 429 or retCode 10006) or a ban of the IP for it (HTTP 403).
        if (result.ResponseStatusCode is null or HttpStatusCode.Forbidden or >= HttpStatusCode.InternalServerError
            || result.Error.ErrorType is ErrorType.RateLimitRequest
            || (code is not null && TransientCodes.Contains(code)))
        {
            return new OrderOutcome(OrderOutcomeKind.Transient, Reason: result.Error.Message);
        }

        return new OrderOutcome(OrderOutcomeKind.Permanent, Reason: result.Error.Message);
    }
}