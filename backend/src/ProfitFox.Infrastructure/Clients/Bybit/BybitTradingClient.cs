using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Objects;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The private calls of Bybit v5 that trade, in the category <c>linear</c> (change take-idea-trades, design.md, decisions
///     3, 4 and 7): the reads <c>/v5/position/list</c>, <c>/v5/execution/list</c> from a moment, <c>/v5/order/realtime</c>
///     then <c>/v5/order/history</c>, <c>/v5/position/closed-pnl</c>; the writes <c>/v5/position/set-leverage</c>,
///     <c>/v5/order/create</c> and <c>/v5/position/trading-stop</c>. The fills are read in windows of seven days from the moment
///     asked for up to now, each with its start and end. A client of <c>Bybit.Net</c> is built for each call with
///     the key of that call and dropped with it, so the secret stays in no cache. One attempt per call: the trading Worker
///     decides on the repeats.
/// </summary>
internal sealed class BybitTradingClient(HttpClient http, BybitSettings settings, ILogger<BybitTradingClient>? logger = null, TimeProvider? clock = null) : IExchangeTrading
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    private const int PageSize = 100;

    /// <summary>
    ///     The widest window of <c>/v5/execution/list</c>: with a start alone Bybit gives the seven days after it, so a read from
    ///     further back goes window by window up to now.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>
    ///     How far past now the last window ends: the clocks of Bybit and of the Worker may differ.
    /// </summary>
    private static readonly TimeSpan Ahead = TimeSpan.FromMinutes(1);

    private readonly BybitCalls _calls = new BybitCalls(http, settings, logger);

    public async Task<IReadOnlyList<ExchangePosition>> PositionsAsync(ExchangeCredentials credentials, string? symbol, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(credentials);
        var positions = new List<ExchangePosition>();
        string? cursor = null;
        do
        {
            BybitResponse<BybitPosition> page = _calls.Read(
                "position-list",
                await client.V5Api.Trading.GetPositionsAsync(Category.Linear, symbol, settleAsset: symbol is null ? "USDT" : null, limit: 200, cursor: cursor, ct: cancellationToken));
            positions.AddRange(page.List.Select(BybitMessages.Of));
            cursor = string.IsNullOrEmpty(page.NextPageCursor) ? null : page.NextPageCursor;
        }
        while (cursor is not null);

        return positions;
    }

    public async Task<IReadOnlyList<ExchangeFill>> FillsAsync(ExchangeCredentials credentials, DateTimeOffset since, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(credentials);
        var fills = new List<ExchangeFill>();
        // The end of the last window is a little after now, and a start ahead of the clock of the Worker — that of Bybit or of
        // the API runs ahead — starts at now: no window ends before it starts.
        DateTimeOffset now = time.GetUtcNow() + Ahead;
        DateTimeOffset start = since < now - Ahead ? since : now - Ahead;
        do
        {
            DateTimeOffset end = start + Window < now ? start + Window : now;
            string? cursor = null;
            do
            {
                BybitResponse<BybitUserTrade> page = _calls.Read(
                    "execution-list",
                    await client.V5Api.Trading.GetUserTradesAsync(Category.Linear, startTime: start.UtcDateTime, endTime: end.UtcDateTime, limit: PageSize, cursor: cursor, ct: cancellationToken));
                fills.AddRange(page.List.Select(BybitMessages.Of));
                cursor = string.IsNullOrEmpty(page.NextPageCursor) ? null : page.NextPageCursor;
            }
            while (cursor is not null);

            start = end;
        }
        while (start < now);

        return [.. fills.DistinctBy(fill => fill.ExecId)];
    }

    public async Task<ExchangeOrderUpdate?> OrderAsync(ExchangeCredentials credentials, string symbol, string linkId, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(credentials);
        BybitResponse<BybitOrder> open = _calls.Read("order-realtime", await client.V5Api.Trading.GetOrdersAsync(Category.Linear, symbol, clientOrderId: linkId, ct: cancellationToken));
        if (open.List.FirstOrDefault(order => order.ClientOrderId == linkId) is { } found)
        {
            return BybitMessages.Of(found);
        }

        BybitResponse<BybitOrder> history = _calls.Read("order-history", await client.V5Api.Trading.GetOrderHistoryAsync(Category.Linear, symbol, clientOrderId: linkId, ct: cancellationToken));
        return history.List.FirstOrDefault(order => order.ClientOrderId == linkId) is { } old ? BybitMessages.Of(old) : null;
    }

    public async Task<ExchangeClosedResult?> ClosedAsync(ExchangeCredentials credentials, string symbol, DateTimeOffset since, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(credentials);
        BybitResponse<BybitClosedPnl> closed = _calls.Read("closed-pnl", await client.V5Api.Trading.GetClosedProfitLossAsync(Category.Linear, symbol, startTime: since.UtcDateTime, ct: cancellationToken));
        BybitClosedPnl[] rows = [.. closed.List.Where(row => row.Symbol == symbol)];
        if (rows.Length == 0)
        {
            return null;
        }

        decimal quantity = rows.Sum(row => row.ClosedSize);
        decimal exitValue = rows.Sum(row => row.ExitValue);
        return new ExchangeClosedResult(
            rows.Sum(row => row.ClosedPnl),
            quantity > 0 ? exitValue / quantity : rows[^1].AverageExitPrice,
            quantity,
            new DateTimeOffset(DateTime.SpecifyKind(rows.Max(row => row.UpdateTime), DateTimeKind.Utc)));
    }

    public async Task<ExchangeAnswer> SetLeverageAsync(ExchangeCredentials credentials, string symbol, int leverage, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(credentials);
        return _calls.Write("set-leverage", await client.V5Api.Account.SetLeverageAsync(Category.Linear, symbol, leverage, leverage, cancellationToken));
    }

    public async Task<ExchangeAnswer> PlaceOrderAsync(ExchangeCredentials credentials, ExchangeOrderRequest order, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(credentials);
        HttpResult<BybitOrderId> result = await client.V5Api.Trading.PlaceOrderAsync(
            Category.Linear,
            order.Symbol,
            BybitMessages.Side(order.Side),
            NewOrderType.Market,
            order.Quantity,
            positionIdx: PositionIdx.OneWayMode,
            clientOrderId: order.LinkId,
            stopLoss: order.Stop,
            takeProfit: order.Target,
            stopLossTriggerBy: order.Stop is null ? null : TriggerType.LastPrice,
            takeProfitTriggerBy: order.Target is null ? null : TriggerType.LastPrice,
            stopLossTakeProfitMode: order.Stop is null && order.Target is null ? null : StopLossTakeProfitMode.Full,
            reduceOnly: order.ReduceOnly ? true : null,
            ct: cancellationToken);
        return _calls.Write("order-create", result, result.Data?.OrderId);
    }

    public async Task<ExchangeAnswer> SetStopAsync(ExchangeCredentials credentials, string symbol, decimal stop, CancellationToken cancellationToken)
    {
        using BybitRestClient client = _calls.Client(credentials);
        return _calls.Write(
            "trading-stop",
            await client.V5Api.Trading.SetTradingStopAsync(
                Category.Linear,
                symbol,
                PositionIdx.OneWayMode,
                stopLoss: stop,
                stopLossTrigger: TriggerType.LastPrice,
                stopLossTakeProfitMode: StopLossTakeProfitMode.Full,
                ct: cancellationToken));
    }
}
