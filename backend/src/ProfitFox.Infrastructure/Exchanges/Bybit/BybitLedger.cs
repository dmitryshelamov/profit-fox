using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

using ExchangeOrderState = ProfitFox.Application.Ports.LiveTrading.ExchangeOrderState;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>Reads what an account holds on Bybit with its key, one attempt per call. Never sends orders.</summary>
internal sealed class BybitLedger(HttpClient http, ILoggerFactory loggers, BybitSettings settings) : IExchangeLedger
{
    // The most orders Bybit lists in one page.
    private const int PageSize = 50;

    public async Task<decimal> GetFreeAsync(ExchangeKey key, string coin, CancellationToken cancellationToken)
    {
        BybitAssetBalance? balance = await CoinAsync(key, coin, cancellationToken);

        return (balance?.WalletBalance ?? 0m) - (balance?.Locked ?? 0m);
    }

    public async Task<bool> IsHedgeModeAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        return (await PositionsAsync(key, instrument, cancellationToken)).Any(position => position.PositionIdx != PositionIdx.OneWayMode);
    }

    public async Task<decimal> GetHoldingAsync(ExchangeKey key, Instrument instrument, string baseCoin, CancellationToken cancellationToken)
    {
        if (instrument.Category == MarketCategory.Spot)
        {
            return (await CoinAsync(key, baseCoin, cancellationToken))?.WalletBalance ?? 0m;
        }

        return (await PositionsAsync(key, instrument, cancellationToken))
            .Sum(position => position.Side switch { PositionSide.Buy => position.Quantity, PositionSide.Sell => -position.Quantity, _ => 0m });
    }

    public async Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(key);
        var orders = new List<ExchangeOrder>();
        string? cursor = null;

        do
        {
            BybitResponse<BybitOrder> page = BybitKeyClients.Unwrap(await client.V5Api.Trading.GetOrdersAsync(
                instrument.Category.ToBybit(), instrument.Symbol, openOnly: 0, limit: PageSize, cursor: cursor, ct: cancellationToken));
            orders.AddRange(page.List.Select(Order));
            cursor = string.IsNullOrEmpty(page.NextPageCursor) ? null : page.NextPageCursor;
        }
        while (cursor is not null);

        return orders;
    }

    public async Task<ExchangeOrder?> FindOrderAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(key);
        BybitOrder? open = BybitKeyClients.Unwrap(await client.V5Api.Trading.GetOrdersAsync(
            instrument.Category.ToBybit(), instrument.Symbol, clientOrderId: clientId, ct: cancellationToken)).List.FirstOrDefault();

        // An order that finished leaves the open orders for the history.
        BybitOrder? order = open ?? BybitKeyClients.Unwrap(await client.V5Api.Trading.GetOrderHistoryAsync(
            instrument.Category.ToBybit(), instrument.Symbol, clientOrderId: clientId, ct: cancellationToken)).List.FirstOrDefault();

        return order is null ? null : Order(order);
    }

    public async Task<IReadOnlyList<ExchangeFill>> GetFillsAsync(ExchangeKey key, Instrument instrument, string clientId, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(key);
        var trades = new List<BybitUserTrade>();
        string? cursor = null;

        // An order filled in many parts lists its fills on more than one page.
        do
        {
            BybitResponse<BybitUserTrade> page = BybitKeyClients.Unwrap(await client.V5Api.Trading.GetUserTradesAsync(
                instrument.Category.ToBybit(), instrument.Symbol, clientOrderId: clientId, cursor: cursor, ct: cancellationToken));
            trades.AddRange(page.List);
            cursor = string.IsNullOrEmpty(page.NextPageCursor) ? null : page.NextPageCursor;
        }
        while (cursor is not null);

        return
        [
            .. trades
                .OrderBy(trade => trade.Timestamp)
                .Select(trade => new ExchangeFill(trade.TradeId, trade.ClientOrderId ?? "", trade.Quantity, trade.Price, trade.Fee ?? 0m, trade.FeeAsset ?? "", Utc(trade.Timestamp), instrument))
        ];
    }

    public async Task<FeeSchedule> GetFeesAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(key);
        BybitFeeRate rate = BybitKeyClients.Unwrap(await client.V5Api.Account.GetFeeRateAsync(instrument.Category.ToBybit(), instrument.Symbol, ct: cancellationToken))
            .List.Single();

        return new FeeSchedule(rate.MakerFeeRate, rate.TakerFeeRate, instrument.Category);
    }

    private async Task<BybitAssetBalance?> CoinAsync(ExchangeKey key, string coin, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(key);
        BybitBalance? wallet = BybitKeyClients.Unwrap(await client.V5Api.Account.GetBalancesAsync(AccountType.Unified, ct: cancellationToken)).List.SingleOrDefault();

        return wallet?.Assets.SingleOrDefault(asset => asset.Asset == coin);
    }

    private async Task<IReadOnlyList<BybitPosition>> PositionsAsync(ExchangeKey key, Instrument instrument, CancellationToken cancellationToken)
    {
        using BybitRestClient client = Client(key);

        return [.. BybitKeyClients.Unwrap(await client.V5Api.Trading.GetPositionsAsync(instrument.Category.ToBybit(), instrument.Symbol, ct: cancellationToken)).List];
    }

    private BybitRestClient Client(ExchangeKey key)
    {
        return BybitKeyClients.Create(http, loggers, settings, key.ApiKey, key.Secret);
    }

    private static ExchangeOrder Order(BybitOrder order)
    {
        ExchangeOrderState state = order.Status switch
        {
            OrderStatus.Filled => ExchangeOrderState.Filled,
            OrderStatus.Cancelled or OrderStatus.PartiallyFilledCanceled or OrderStatus.Deactivated => ExchangeOrderState.Cancelled,
            OrderStatus.Rejected => ExchangeOrderState.Rejected,
            _ => ExchangeOrderState.Open
        };

        return new ExchangeOrder(
            order.ClientOrderId ?? "",
            order.OrderId,
            state,
            order.QuantityFilled ?? 0m,
            state == ExchangeOrderState.Rejected ? order.RejectReason : null,
            order.ValueFilled ?? 0m,
            order.ExecutedFee ?? 0m,
            order.FeeAsset ?? "",
            string.IsNullOrEmpty(order.ParentOrderId) ? null : order.ParentOrderId,
            order.StopOrderType.ToStopKind());
    }

    private static DateTimeOffset Utc(DateTime time)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }
}