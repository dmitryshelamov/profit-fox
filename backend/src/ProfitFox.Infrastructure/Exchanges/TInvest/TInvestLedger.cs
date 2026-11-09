using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.ValueObjects.Strategies;

using Tinkoff.InvestApi.V1;

using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     What an account holds on T-Invest, read with the token of its key, one attempt per call (decision 8): its free money, its shares,
///     its active orders told by their request ids, an order of the system by its request id and its trades, and the fee rate of a trial
///     order. Never sends an order. An order T-Invest does not know, such as one of a day gone, is <c>null</c>, never read again.
/// </summary>
internal sealed class TInvestLedger(TInvestCall call, TInvestShares shares, ITInvestOrderIds ids) : IExchangeLedger
{
    // The commission of T-Invest is counted in whole kopecks: a trial order of at least this many roubles tells the rate closely.
    private const decimal TrialAmount = 100_000m;

    public async Task<decimal> GetFreeAsync(ExchangeKey key, string coin, CancellationToken cancellationToken)
    {
        PositionsResponse positions = await PositionsAsync(key, cancellationToken);

        return Of(positions.Money, coin) - Of(positions.Blocked, coin);
    }

    /// <summary>A share has one position on T-Invest.</summary>
    public Task<bool> IsHedgeModeAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <summary>The shares of the account, those blocked by its orders included.</summary>
    public async Task<decimal> GetHoldingAsync(ExchangeKey key, Instrument instrument, string baseCoin, CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);
        PositionsResponse positions = await PositionsAsync(key, cancellationToken);

        return positions.Securities.Where(security => security.InstrumentUid == share.Uid).Sum(security => (decimal)(security.Balance + security.Blocked));
    }

    /// <summary>The shares of MOEX have no symbol of <c>linear</c>: refused without a call.</summary>
    public Task<LinearPosition> GetLinearPositionAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        throw new ExchangeException("T-Invest has no leverage of a symbol.");
    }

    public async Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);
        GetOrdersResponse response = await call.OnceAsync(
            key.Secret,
            invoker => new OrdersService.OrdersServiceClient(invoker).GetOrdersAsync(new GetOrdersRequest { AccountId = key.ApiKey }),
            cancellationToken);
        var orders = new List<ExchangeOrder>();

        foreach (OrderState state in response.Orders.Where(order => order.InstrumentUid == share.Uid))
        {
            orders.Add(TInvestOrderStates.Order(state, await ClientIdOfAsync(state, cancellationToken), share.Lot));
        }

        return orders;
    }

    public async Task<ExchangeOrder?> FindOrderAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        if (await ids.RequestOfAsync(clientId, cancellationToken) is not { } request
            || await TInvestOrderReads.StateAsync(call, key, request.ToString(), OrderIdType.Request, cancellationToken) is not { } state)
        {
            return null;
        }

        return TInvestOrderStates.Order(state, clientId, (await shares.ShareByAsync(instrument.Symbol, cancellationToken)).Lot);
    }

    public async Task<ExchangeOrder?> FindOrderByExchangeIdAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken)
    {
        if (await TInvestOrderReads.StateAsync(call, key, exchangeOrderId, OrderIdType.Exchange, cancellationToken) is not { } state)
        {
            return null;
        }

        return TInvestOrderStates.Order(state, await ClientIdOfAsync(state, cancellationToken), (await shares.ShareByAsync(instrument.Symbol, cancellationToken)).Lot);
    }

    /// <summary>T-Invest makes no orders of an order in this change: no stop and no target of a trade by an idea.</summary>
    public Task<IReadOnlyList<ExchangeOrder>> GetOrdersOfParentAsync(ExchangeKey key, Instrument instrument, string parentClientId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ExchangeOrder>>([]);
    }

    public async Task<IReadOnlyList<ExchangeFill>> GetOrderFillsAsync(ExchangeKey key, Instrument instrument, string exchangeOrderId, CancellationToken cancellationToken)
    {
        if (await TInvestOrderReads.StateAsync(call, key, exchangeOrderId, OrderIdType.Exchange, cancellationToken) is not { } state)
        {
            return [];
        }

        return TInvestOrderStates.Fills(state, await ClientIdOfAsync(state, cancellationToken), (await shares.ShareByAsync(instrument.Symbol, cancellationToken)).Lot, instrument);
    }

    public async Task<IReadOnlyList<ExchangeFill>> GetFillsAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        if (await ids.RequestOfAsync(clientId, cancellationToken) is not { } request
            || await TInvestOrderReads.StateAsync(call, key, request.ToString(), OrderIdType.Request, cancellationToken) is not { } state)
        {
            return [];
        }

        return TInvestOrderStates.Fills(state, clientId, (await shares.ShareByAsync(instrument.Symbol, cancellationToken)).Lot, instrument);
    }

    /// <summary>
    ///     The rate is the commission of a trial limit buy of whole lots for at least <see cref="TrialAmount" /> at the last price, by
    ///     <c>GetOrderPrice</c>, which places nothing; T-Invest has no method of the tariff. A limit and a market order pay the same.
    /// </summary>
    /// <exception cref="ExchangeUnavailableException">T-Invest told no last price of the share or no amount of the trial order.</exception>
    /// <summary>The shares of MOEX are not liquidated in this trading: refused without a call.</summary>
    public Task<IReadOnlyList<ExchangeFill>> GetForcedFillsAsync(ExchangeKey key, Instrument instrument, DateTimeOffset since, CancellationToken cancellationToken)
    {
        throw new ExchangeException("T-Invest has no liquidations.");
    }

    public async Task<FeeSchedule> GetFeesAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        TInvestShare share = await shares.ShareByAsync(instrument.Symbol, cancellationToken);
        GetLastPricesResponse last = await call.OnceAsync(
            key.Secret,
            invoker => new MarketDataService.MarketDataServiceClient(invoker).GetLastPricesAsync(new GetLastPricesRequest { InstrumentId = { share.Uid } }),
            cancellationToken);
        decimal price = last.LastPrices.SingleOrDefault(item => item.InstrumentUid == share.Uid)?.Price?.ToDecimal() ?? 0m;

        if (price <= 0m)
        {
            throw new ExchangeUnavailableException($"T-Invest told no last price of {instrument.Symbol}: the fee rate waits for one.");
        }

        var trial = new GetOrderPriceRequest
        {
            AccountId = key.ApiKey,
            InstrumentId = share.Uid,
            Price = price.ToQuotation(),
            Direction = OrderDirection.Buy,
            Quantity = (long)Math.Ceiling(TrialAmount / (price * share.Lot))
        };
        GetOrderPriceResponse estimate = await call.OnceAsync(key.Secret, invoker => new OrdersService.OrdersServiceClient(invoker).GetOrderPriceAsync(trial), cancellationToken);
        decimal amount = estimate.InitialOrderAmount?.ToDecimal() ?? 0m;

        if (amount <= 0m)
        {
            throw new ExchangeUnavailableException($"T-Invest told no amount of the trial order of {instrument.Symbol}: the fee rate waits for one.");
        }

        decimal rate = estimate.DealCommission.ToDecimal() / amount;

        return new FeeSchedule(rate, rate, instrument.Category);
    }

    private Task<PositionsResponse> PositionsAsync(ExchangeKey key, CancellationToken cancellationToken)
    {
        return call.OnceAsync(
            key.Secret,
            invoker => new OperationsService.OperationsServiceClient(invoker).GetPositionsAsync(new PositionsRequest { AccountId = key.ApiKey }),
            cancellationToken);
    }

    private static decimal Of(IEnumerable<MoneyValue> money, string coin)
    {
        return money.Where(item => string.Equals(item.Currency, coin, StringComparison.OrdinalIgnoreCase)).Sum(item => item.ToDecimal());
    }

    /// <summary>The client id of an order of the system by its request id; <c>""</c> for an order of nobody.</summary>
    private async Task<string> ClientIdOfAsync(OrderState state, CancellationToken cancellationToken)
    {
        return Guid.TryParse(state.OrderRequestId, out Guid request) ? await ids.ClientIdOfAsync(request, cancellationToken) ?? "" : "";
    }
}