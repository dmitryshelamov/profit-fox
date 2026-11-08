using System.Globalization;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.LiveTrading;

using Tinkoff.InvestApi.V1;

using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;
using OrderSide = ProfitFox.Domain.Entities.Strategies.Enums.OrderSide;
using OrderType = ProfitFox.Domain.Entities.Strategies.Enums.OrderType;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The orders and cancels of an account on T-Invest, one attempt per call (decisions 4, 5 and 6): an order goes in lots, a limit one
///     for the day and a market one without a price, under the request id written before it goes, and only while the session takes it;
///     what T-Invest refuses is told by its code, never by its text.
/// </summary>
internal sealed class TInvestOrderGateway(TInvestCall call, TInvestShares shares, ITInvestOrderIds ids, TInvestTradingStatuses statuses, TimeProvider time) : IOrderGateway
{
    private const int RequestKnown = 30057;
    private const int SessionClosed = 30098;
    private const int OutOfCorridor = 30099;

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public async Task<OrderOutcome> PlaceAsync(ExchangeKey key, Instrument instrument, OrderRequest order, CancellationToken cancellationToken)
    {
        if (order.Protection is not null || order.ReduceOnly || order.Quantity is not { } quantity)
        {
            return new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "T-Invest takes only orders by quantity without a stop, a target or reduce-only.");
        }

        try
        {
            TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);

            if (quantity % share.Lot != 0m)
            {
                return new OrderOutcome(
                    OrderOutcomeKind.Permanent,
                    Reason: $"The order is not a whole number of lots: {quantity.ToString(CultureInfo.InvariantCulture)} shares at a lot of {share.Lot}.");
            }

            switch (await statuses.MayPlaceAsync(key.Secret, share, order.Type, order.Price, cancellationToken))
            {
                case TInvestPlacement.SessionClosed:
                    return await UntilSessionAsync(key, share, cancellationToken);
                case TInvestPlacement.OutOfCorridor:
                    return new OrderOutcome(OrderOutcomeKind.Transient, RetryAfter: Minute);
            }

            Guid request = await ids.EnsureAsync(order.ClientId, key.ApiKey, share.Uid, cancellationToken);

            try
            {
                PostOrderResponse posted = await call.OnceAsync(
                    key.Secret,
                    invoker => new OrdersService.OrdersServiceClient(invoker).PostOrderAsync(Post(key, share, order, quantity, request)),
                    cancellationToken);

                return await TakenAsync(order.ClientId, posted.OrderId, cancellationToken);
            }
            catch (TInvestRefusedException exception) when (exception.Code == RequestKnown)
            {
                // The request was taken before, its answer lost: the order is the one T-Invest has under the request id, once its state
                // knows the request (ADR 0036); until then the send is tried again with the same request id.
                return await TInvestOrderReads.StateAsync(call, key, request.ToString(), OrderIdType.Request, cancellationToken) is { } known
                    ? await TakenAsync(order.ClientId, known.OrderId, cancellationToken)
                    : new OrderOutcome(OrderOutcomeKind.Transient);
            }
            catch (TInvestRefusedException exception) when (exception.Code == SessionClosed)
            {
                return await UntilSessionAsync(key, share, cancellationToken);
            }
            catch (TInvestRefusedException exception) when (exception.Code == OutOfCorridor)
            {
                return new OrderOutcome(OrderOutcomeKind.Transient, RetryAfter: Minute);
            }
            catch (TInvestRefusedException exception) when (exception.Code is { } code)
            {
                return new OrderOutcome(OrderOutcomeKind.Permanent, Reason: $"T-Invest refused the order: code {code}.");
            }
        }
        catch (ExchangeException exception)
        {
            return Failure(exception);
        }
    }

    public Task<OrderOutcome> CancelAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        return CancelAsync(key, async () => (await ids.RequestOfAsync(clientId, cancellationToken))?.ToString(), OrderIdType.Request, cancellationToken);
    }

    public Task<OrderOutcome> CancelByExchangeIdAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken)
    {
        return CancelAsync(key, () => Task.FromResult<string?>(exchangeOrderId), OrderIdType.Exchange, cancellationToken);
    }

    private async Task<OrderOutcome> CancelAsync(ExchangeKey key, Func<Task<string?>> idOf, OrderIdType type, CancellationToken cancellationToken)
    {
        // An order that never went to T-Invest is not there to cancel.
        if (await idOf() is not { } id)
        {
            return new OrderOutcome(OrderOutcomeKind.AlreadyDone);
        }

        try
        {
            var request = new CancelOrderRequest { AccountId = key.ApiKey, OrderId = id, OrderIdType = type };
            await call.OnceAsync(key.Secret, invoker => new OrdersService.OrdersServiceClient(invoker).CancelOrderAsync(request), cancellationToken);

            return new OrderOutcome(OrderOutcomeKind.Accepted);
        }
        catch (TInvestNotFoundException)
        {
            return new OrderOutcome(OrderOutcomeKind.AlreadyDone);
        }
        catch (TInvestRefusedException exception) when (exception.Code is { } code)
        {
            // A refusal of a cancel of an order that ended meanwhile, or that T-Invest does not know: it is done already.
            OrderState? state = await TInvestOrderReads.StateAsync(call, key, id, type, cancellationToken);

            return state is null or
            {
                ExecutionReportStatus: OrderExecutionReportStatus.ExecutionReportStatusFill
                    or OrderExecutionReportStatus.ExecutionReportStatusCancelled
                    or OrderExecutionReportStatus.ExecutionReportStatusRejected
            }
                ? new OrderOutcome(OrderOutcomeKind.AlreadyDone)
                : new OrderOutcome(OrderOutcomeKind.Permanent, Reason: $"T-Invest refused the cancel: code {code}.");
        }
        catch (ExchangeException exception)
        {
            return Failure(exception);
        }
    }

    private static PostOrderRequest Post(ExchangeKey key, TInvestShare share, OrderRequest order, decimal quantity, Guid request)
    {
        var post = new PostOrderRequest
        {
            AccountId = key.ApiKey,
            InstrumentId = share.Uid,
            Quantity = (long)(quantity / share.Lot),
            Direction = order.Side == OrderSide.Buy ? OrderDirection.Buy : OrderDirection.Sell,
            OrderType = order.Type == OrderType.Limit ? Tinkoff.InvestApi.V1.OrderType.Limit : Tinkoff.InvestApi.V1.OrderType.Market,
            OrderId = request.ToString()
        };

        if (order.Type == OrderType.Limit)
        {
            post.Price = order.Price!.Value.ToQuotation();
            post.TimeInForce = TimeInForceType.TimeInForceDay;
        }

        return post;
    }

    private async Task<OrderOutcome> TakenAsync(string clientId, string exchangeId, CancellationToken cancellationToken)
    {
        await ids.SetExchangeIdAsync(clientId, exchangeId, cancellationToken);

        return new OrderOutcome(OrderOutcomeKind.Accepted, exchangeId);
    }

    /// <summary>The next attempt waits for the opening of the next session by the schedule, or a minute without one.</summary>
    private async Task<OrderOutcome> UntilSessionAsync(ExchangeKey key, TInvestShare share, CancellationToken cancellationToken)
    {
        DateTimeOffset? opening = await statuses.NextSessionStartAsync(key.Secret, share.Exchange, cancellationToken);

        return new OrderOutcome(OrderOutcomeKind.Transient, RetryAfter: opening is { } start ? start - time.GetUtcNow() : Minute);
    }

    /// <summary>A token T-Invest does not accept is a refusal for good; no answer, a rate limit or a failure of T-Invest a transient one.</summary>
    private static OrderOutcome Failure(ExchangeException exception)
    {
        return exception is ExchangeUnavailableException
            ? new OrderOutcome(OrderOutcomeKind.Transient, Reason: exception.Message)
            : new OrderOutcome(OrderOutcomeKind.Permanent, Reason: exception.Message);
    }
}