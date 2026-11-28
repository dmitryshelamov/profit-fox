using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Integration.Exchange;

/// <summary>
///     Bybit in memory for the tests of the trades (change take-idea-trades, design.md, decision 15), plugged into the ports of
///     the trades and of the streams, not into HTTP or a socket. It keeps the prices and rules of the pairs, and for each key
///     the positions, the orders and the fills; it takes market orders with the stop and the target of the position, fills
///     them at the price now, and fills the stop and the target itself when the price passes them. It can liquidate, deleverage,
///     close or add by hand, refuse with a given code, keep silent, and take a repeated link id as Bybit does. It is the
///     stream too: it sends the updates of orders, fills and positions to the open streams of a key and the mark prices to the
///     streams of the prices; it can drop a stream, keep silent about an event while dropped or alive, bring the stream back,
///     send a fill before its order and send it twice. Every call is recorded.
/// </summary>
public sealed class FakeBybit : IExchangeMarket, IExchangeTrading, IExchangeStream, IExchangePriceStream, IExchangeKeyInspector
{
    public const decimal TakerFee = 0.00055m;

    private readonly Lock _gate = new Lock();
    private readonly Dictionary<string, PairState> _pairs = new Dictionary<string, PairState>(StringComparer.Ordinal);
    private readonly Dictionary<string, Account> _accounts = new Dictionary<string, Account>(StringComparer.Ordinal);
    private readonly List<PriceStream> _prices = [];
    private int _sequence;

    /// <summary>
    ///     Every call as its name, the key — empty for a public call — and the pair, when it has one.
    /// </summary>
    public List<(string Call, string ApiKey, string? Symbol)> Calls { get; } = [];

    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>
    ///     The moment of an event of the exchange: whole milliseconds, as Bybit gives them.
    /// </summary>
    private DateTimeOffset Moment()
    {
        DateTimeOffset now = Time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    /// <summary>
    ///     How long opening a stream takes; a stream of a key whose <see cref="Account.StreamRefusal" /> is set does not open.
    /// </summary>
    public TimeSpan OpenDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Whether the streams of the prices open.
    /// </summary>
    public bool PricesOpen { get; set; } = true;

    /// <summary>
    ///     The public reads — the price and the marks — do not answer.
    /// </summary>
    public bool MarketUnavailable { get; set; }

    /// <summary>
    ///     The calls so far, in their order.
    /// </summary>
    public IReadOnlyList<(string Call, string ApiKey, string? Symbol)> CallsNow()
    {
        lock (_gate)
        {
            return [.. Calls];
        }
    }

    public int RestCalls
    {
        get
        {
            lock (_gate)
            {
                return Calls.Count;
            }
        }
    }

    /// <summary>
    ///     Puts the fake in place of every port of Bybit in <paramref name="services" />.
    /// </summary>
    public void Use(IServiceCollection services)
    {
        services.AddSingleton<IExchangeMarket>(this);
        services.AddSingleton<IExchangeTrading>(this);
        services.AddSingleton<IExchangeStream>(this);
        services.AddSingleton<IExchangePriceStream>(this);
        services.AddSingleton<IExchangeKeyInspector>(this);
    }

    // ------------------------------------------------------------------ the market

    public FakeBybit Pair(string symbol, decimal price, decimal step = 0.1m, decimal minQuantity = 0.1m, decimal minNotional = 5m, decimal tick = 0.01m, decimal maxLeverage = 50m, bool trading = true)
    {
        lock (_gate)
        {
            _pairs[symbol] = new PairState(new PairRules(symbol, step, minQuantity, minNotional, tick, maxLeverage, trading), price);
        }

        return this;
    }

    /// <summary>
    ///     Moves the price of the pair: the mark goes to the streams of the prices, and every stop and target it passes is filled
    ///     by the exchange at its level.
    /// </summary>
    public void Price(string symbol, decimal price)
    {
        lock (_gate)
        {
            _pairs[symbol].Price = price;
            foreach (PriceStream stream in _prices.Where(stream => stream.Symbol == symbol && stream.Live))
            {
                stream.OnMark(symbol, price);
            }

            foreach (Account account in _accounts.Values)
            {
                if (account.Positions.TryGetValue(symbol, out Position? position) && position.Size > 0)
                {
                    int sign = (int)position.Side;
                    if (position.Stop is { } stop && (price - stop) * sign <= 0)
                    {
                        Fill(account, symbol, Opposite(position.Side), position.Size, Slipped(stop, position.Side), null, ExchangeFillType.Trade, ExchangeStopType.StopLoss);
                    }
                    else if (position.Target is { } target && (price - target) * sign >= 0)
                    {
                        Fill(account, symbol, Opposite(position.Side), position.Size, Slipped(target, position.Side), null, ExchangeFillType.Trade, ExchangeStopType.TakeProfit);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     How much worse than its level, as a fraction, the market order of a stop or a target fills (review R8).
    /// </summary>
    public decimal StopSlippage { get; set; }

    // The fill price of the stop or the target of a position of the side: worse for the position by the slippage.
    private decimal Slipped(decimal level, TradeSide side)
    {
        return level * (1 - (int)side * StopSlippage);
    }

    public decimal PriceOf(string symbol)
    {
        lock (_gate)
        {
            return _pairs[symbol].Price;
        }
    }

    // ------------------------------------------------------------------ the accounts

    /// <summary>
    ///     The account of the key: of the isolated margin, with 1 250.40 free USDT and a key that trades.
    /// </summary>
    public Account Key(string apiKey)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(apiKey, out Account? account))
            {
                account = new Account(this, apiKey);
                _accounts[apiKey] = account;
            }

            return account;
        }
    }

    /// <summary>
    ///     The position of the pair on the account of the key; null when there is none.
    /// </summary>
    public Position? PositionOf(string apiKey, string symbol)
    {
        lock (_gate)
        {
            return Key(apiKey).Positions.TryGetValue(symbol, out Position? position) && position.Size > 0 ? position with { } : null;
        }
    }

    public IReadOnlyList<ExchangeFill> FillsOf(string apiKey)
    {
        lock (_gate)
        {
            return [.. Key(apiKey).Fills];
        }
    }

    public IReadOnlyList<(ExchangeOrderRequest Request, ExchangeOrderUpdate Update)> OrdersOf(string apiKey)
    {
        lock (_gate)
        {
            return [.. Key(apiKey).Orders.Values];
        }
    }

    /// <summary>
    ///     The exchange liquidates the position: a fill at the given price without an order of the account.
    /// </summary>
    public void Liquidate(string apiKey, string symbol, decimal price, bool adl = false)
    {
        lock (_gate)
        {
            Account account = Key(apiKey);
            Position position = account.Positions[symbol];
            Fill(account, symbol, Opposite(position.Side), position.Size, price, null, adl ? ExchangeFillType.Adl : ExchangeFillType.Liquidation, ExchangeStopType.None, fee: 0m);
        }
    }

    /// <summary>
    ///     The owner trades the pair in the terminal of Bybit: a fill at the price now with an order that is not of the bench.
    /// </summary>
    public void ByHand(string apiKey, string symbol, TradeSide side, decimal quantity)
    {
        lock (_gate)
        {
            Fill(Key(apiKey), symbol, side, quantity, _pairs[symbol].Price, $"web-{Interlocked.Increment(ref _sequence)}", ExchangeFillType.Trade, ExchangeStopType.None);
        }
    }

    /// <summary>
    ///     A payment of funding of the position: <paramref name="paid" /> by the account, negative when it receives.
    /// </summary>
    public void Funding(string apiKey, string symbol, decimal paid)
    {
        lock (_gate)
        {
            Account account = Key(apiKey);
            Position position = account.Positions[symbol];
            var fill = new ExchangeFill(NextId(), symbol, null, position.Side, _pairs[symbol].Price, position.Size, paid, ExchangeFillType.Funding, ExchangeStopType.None, Moment());
            account.Fills.Add(fill);
            account.Send(fill);
        }
    }

    /// <summary>
    ///     The stream of the key tells <paramref name="message" />, true or not.
    /// </summary>
    public void Tell(string apiKey, ExchangeMessage message)
    {
        lock (_gate)
        {
            Key(apiKey).Send(message);
        }
    }

    /// <summary>
    ///     The fills of the pair on the side are no longer in the reads of REST, as when Bybit keeps them out of its window.
    /// </summary>
    public void HideFills(string apiKey, string symbol, TradeSide side)
    {
        lock (_gate)
        {
            Key(apiKey).Fills.RemoveAll(fill => fill.Symbol == symbol && fill.Side == side);
        }
    }

    /// <summary>
    ///     The owner moves or removes the stop of the position on Bybit.
    /// </summary>
    public void StopByHand(string apiKey, string symbol, decimal? stop)
    {
        lock (_gate)
        {
            Account account = Key(apiKey);
            Position position = account.Positions[symbol];
            position.Stop = stop;
            account.Send(Snapshot(symbol, position));
        }
    }

    // ------------------------------------------------------------------ IExchangeKeyInspector

    public Task<ExchangeKeyInfo> InspectAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("query-api", apiKey, null);
            Account account = Key(apiKey);
            account.Refusal?.Invoke();
            return Task.FromResult(account.KeyInfo);
        }
    }

    public Task<decimal> FreeAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("wallet-balance", apiKey, null);
            Account account = Key(apiKey);
            account.Refusal?.Invoke();
            return Task.FromResult(account.Free);
        }
    }

    public Task<string?> MarginModeAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("account-info", apiKey, null);
            Account account = Key(apiKey);
            account.Refusal?.Invoke();
            return Task.FromResult(account.MarginMode);
        }
    }

    // ------------------------------------------------------------------ IExchangeMarket

    public Task<decimal?> PriceAsync(string symbol, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("tickers", "", symbol);
            if (MarketUnavailable)
            {
                throw new ExchangeUnavailableException("Bybit is unavailable: no answer");
            }

            return Task.FromResult(_pairs.TryGetValue(symbol, out PairState? pair) ? pair.Price : (decimal?)null);
        }
    }

    public Task<PairRules?> RulesAsync(string symbol, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("instruments-info", "", symbol);
            return Task.FromResult(_pairs.TryGetValue(symbol, out PairState? pair) ? pair.Rules : null);
        }
    }

    public Task<IReadOnlyDictionary<string, decimal>> MarksAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("tickers", "", null);
            if (MarketUnavailable)
            {
                throw new ExchangeUnavailableException("Bybit is unavailable: no answer");
            }

            return Task.FromResult<IReadOnlyDictionary<string, decimal>>(symbols.Where(_pairs.ContainsKey).ToDictionary(symbol => symbol, symbol => _pairs[symbol].Price));
        }
    }

    // ------------------------------------------------------------------ IExchangeTrading

    public Task<IReadOnlyList<ExchangePosition>> PositionsAsync(ExchangeCredentials credentials, string? symbol, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("position-list", credentials.ApiKey, symbol);
            Account account = Read(credentials);
            IReadOnlyList<ExchangePosition> positions = symbol is null
                ? [.. account.Positions.Where(pair => pair.Value.Size > 0).Select(pair => Snapshot(pair.Key, pair.Value))]
                : [account.Positions.TryGetValue(symbol, out Position? position) ? Snapshot(symbol, position) : ExchangePosition.Empty(symbol) with { OneWay = !account.Hedge.Contains(symbol) }];
            return Task.FromResult(positions);
        }
    }

    public Task<IReadOnlyList<ExchangeFill>> FillsAsync(ExchangeCredentials credentials, DateTimeOffset since, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("execution-list", credentials.ApiKey, null);
            Key(credentials.ApiKey).FillsSince.Add(since);
            return Task.FromResult<IReadOnlyList<ExchangeFill>>([.. Read(credentials).Fills.Where(fill => fill.Time >= since)]);
        }
    }

    public Task<ExchangeOrderUpdate?> OrderAsync(ExchangeCredentials credentials, string symbol, string linkId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("order", credentials.ApiKey, symbol);
            if (Read(credentials).HistoryLags)
            {
                return Task.FromResult<ExchangeOrderUpdate?>(null);
            }

            return Task.FromResult(Read(credentials).Orders.TryGetValue(linkId, out (ExchangeOrderRequest, ExchangeOrderUpdate Update) order) ? order.Update : null);
        }
    }

    public Task<ExchangeClosedResult?> ClosedAsync(ExchangeCredentials credentials, string symbol, DateTimeOffset since, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("closed-pnl", credentials.ApiKey, symbol);
            ExchangeClosedResult[] closed = [.. Read(credentials).Closed.Where(row => row.Symbol == symbol && row.Result.ClosedAt >= since).Select(row => row.Result)];
            return Task.FromResult(closed.Length == 0
                ? null
                : new ExchangeClosedResult(closed.Sum(row => row.Result), closed[^1].ExitPrice, closed.Sum(row => row.Quantity), closed[^1].ClosedAt));
        }
    }

    public Task<ExchangeAnswer> SetLeverageAsync(ExchangeCredentials credentials, string symbol, int leverage, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("set-leverage", credentials.ApiKey, symbol);
            if (Write(credentials) is not { } account)
            {
                return Task.FromResult(NoAnswer);
            }

            if (account.Next("set-leverage") is { } scripted)
            {
                return Task.FromResult(scripted);
            }

            if (leverage > _pairs[symbol].Rules.MaxLeverage)
            {
                return Task.FromResult(Invalid($"leverage {leverage} is above the max leverage {_pairs[symbol].Rules.MaxLeverage}"));
            }

            if (account.Leverage.TryGetValue(symbol, out int now) && now == leverage)
            {
                return Task.FromResult(new ExchangeAnswer(ExchangeAnswerKind.Unchanged, "110043", "leverage not modified"));
            }

            account.Leverage[symbol] = leverage;
            return Task.FromResult(ExchangeAnswer.Accepted);
        }
    }

    public Task<ExchangeAnswer> PlaceOrderAsync(ExchangeCredentials credentials, ExchangeOrderRequest order, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("order-create", credentials.ApiKey, order.Symbol);
            if (Write(credentials) is not { } account)
            {
                return Task.FromResult(NoAnswer);
            }

            PairRules rules = _pairs[order.Symbol].Rules;
            if (!OnStep(order.Quantity, rules.QuantityStep) || order.Quantity < rules.MinQuantity)
            {
                return Task.FromResult(Invalid($"Qty invalid: {order.Quantity} is not on the step {rules.QuantityStep} or below {rules.MinQuantity}"));
            }

            if (!order.ReduceOnly && Level(rules, order.Side, _pairs[order.Symbol].Price, order.Stop, order.Target) is { } wrong)
            {
                return Task.FromResult(Invalid(wrong));
            }

            ExchangeAnswer? scripted = account.Next("order-create");
            if (scripted is { Kind: not ExchangeAnswerKind.Accepted } && scripted.Code != "lost")
            {
                return Task.FromResult(scripted);
            }

            if (account.Orders.ContainsKey(order.LinkId))
            {
                return Task.FromResult(new ExchangeAnswer(ExchangeAnswerKind.Duplicate, "110072", "OrderLinkedID is duplicate"));
            }

            string id = $"order-{Interlocked.Increment(ref _sequence)}";
            decimal price = _pairs[order.Symbol].Price;
            account.Positions.TryGetValue(order.Symbol, out Position? held);
            decimal quantity = order.ReduceOnly ? Math.Min(order.Quantity, held?.Side != order.Side ? held?.Size ?? 0m : 0m) : order.Quantity;
            bool part = order.ReduceOnly && account.PartialClose is { } share && quantity > 0;
            if (part)
            {
                // The book fills a part of the close and the rest is cancelled.
                quantity = rules.QuantityStep > 0 ? decimal.Floor(quantity * account.PartialClose!.Value / rules.QuantityStep) * rules.QuantityStep : quantity * account.PartialClose!.Value;
                account.PartialClose = null;
            }

            bool told = part && account.PartialReportsFilled;
            account.PartialReportsFilled = false;
            var filled = new ExchangeOrderUpdate(order.Symbol, order.LinkId, id, quantity > 0 && (!part || told) ? ExchangeOrderState.Filled : ExchangeOrderState.Cancelled, quantity, quantity > 0 ? null : "EC_ReduceOnlyNoPosition");
            account.Orders[order.LinkId] = (order, filled);
            if (quantity > 0)
            {
                if (account.FillBeforeOrder)
                {
                    Fill(account, order.Symbol, order.Side, quantity, price, order.LinkId, ExchangeFillType.Trade, ExchangeStopType.None, request: order);
                    account.Send(filled);
                }
                else
                {
                    account.Send(filled with { State = ExchangeOrderState.New, Filled = 0m });
                    Fill(account, order.Symbol, order.Side, quantity, price, order.LinkId, ExchangeFillType.Trade, ExchangeStopType.None, request: order);
                    account.Send(filled);
                }
            }
            else
            {
                account.Send(filled);
            }

            // «Ответ потерялся»: the order is taken, the answer does not come.
            return Task.FromResult(scripted is { Code: "lost" } ? new ExchangeAnswer(ExchangeAnswerKind.Temporary, null, "no answer") : new ExchangeAnswer(ExchangeAnswerKind.Accepted, OrderId: id));
        }
    }

    public Task<ExchangeAnswer> SetStopAsync(ExchangeCredentials credentials, string symbol, decimal stop, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("trading-stop", credentials.ApiKey, symbol);
            if (Write(credentials) is not { } account)
            {
                return Task.FromResult(NoAnswer);
            }

            if (account.Next("trading-stop") is { } scripted)
            {
                return Task.FromResult(scripted);
            }

            if (!account.Positions.TryGetValue(symbol, out Position? position) || position.Size == 0)
            {
                return Task.FromResult(new ExchangeAnswer(ExchangeAnswerKind.Refused, "10001", "can not set tp/sl/ts for zero position"));
            }

            if (Level(_pairs[symbol].Rules, position.Side, _pairs[symbol].Price, stop, null) is { } wrong)
            {
                return Task.FromResult(Invalid(wrong));
            }

            position.Stop = stop;
            account.Send(Snapshot(symbol, position));
            return Task.FromResult(ExchangeAnswer.Accepted);
        }
    }

    // ------------------------------------------------------------------ the streams

    public async Task<IExchangeSubscription> OpenAsync(ExchangeCredentials credentials, Action<ExchangeMessage> onMessage, Action<ExchangeStreamState> onState, CancellationToken cancellationToken)
    {
        if (OpenDelay > TimeSpan.Zero)
        {
            await Task.Delay(OpenDelay, cancellationToken);
        }

        lock (_gate)
        {
            Record("stream", credentials.ApiKey, null);
            Account account = Key(credentials.ApiKey);
            if (account.StreamRefusal is { } refusal)
            {
                throw new ExchangeException(refusal);
            }

            var stream = new AccountStream(this, account, onMessage, onState);
            account.Streams.Add(stream);
            return stream;
        }
    }

    public Task<IExchangeSubscription> OpenAsync(string symbol, Action<string, decimal> onMark, Action<ExchangeStreamState> onState, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Record("prices", "", symbol);
            if (!PricesOpen)
            {
                throw new ExchangeException("Bybit не открыл поток цен");
            }

            var stream = new PriceStream(this, symbol, onMark, onState);
            _prices.Add(stream);
            return Task.FromResult<IExchangeSubscription>(stream);
        }
    }

    /// <summary>
    ///     The streams of the prices open now, by pair.
    /// </summary>
    public IReadOnlyList<string> PriceSymbols()
    {
        lock (_gate)
        {
            return [.. _prices.Where(stream => stream.Live).Select(stream => stream.Symbol)];
        }
    }

    /// <summary>
    ///     The connection of the prices is lost, or comes back.
    /// </summary>
    public void PricesLost(bool lost)
    {
        lock (_gate)
        {
            foreach (PriceStream stream in _prices.Where(stream => !stream.Disposed))
            {
                stream.Live = !lost;
                stream.OnState(lost ? ExchangeStreamState.Lost : ExchangeStreamState.Restored);
            }
        }
    }

    // ------------------------------------------------------------------ inside

    private void Fill(Account account, string symbol, TradeSide side, decimal quantity, decimal price, string? linkId, ExchangeFillType type, ExchangeStopType stopType, ExchangeOrderRequest? request = null, decimal? fee = null)
    {
        if (!account.Positions.TryGetValue(symbol, out Position? position) || position.Size == 0)
        {
            position = new Position(side, 0m, 0m, null, null, account.Leverage.GetValueOrDefault(symbol, 10));
            account.Positions[symbol] = position;
        }

        decimal paid = fee ?? decimal.Round(quantity * price * TakerFee, 8);
        if (position.Side == side)
        {
            position.Average = position.Size + quantity == 0 ? price : (position.Average * position.Size + price * quantity) / (position.Size + quantity);
            position.Size += quantity;
            if (request is { ReduceOnly: false })
            {
                position.Stop = request.Stop ?? position.Stop;
                position.Target = request.Target ?? position.Target;
            }
        }
        else
        {
            decimal closed = Math.Min(quantity, position.Size);
            decimal result = (price - position.Average) * closed * (int)position.Side - paid;
            position.Size -= closed;
            if (position.Size == 0)
            {
                account.Closed.Add((symbol, new ExchangeClosedResult(result, price, closed, Moment())));
                position.Stop = null;
                position.Target = null;
            }
        }

        var fill = new ExchangeFill(NextId(), symbol, linkId, side, price, quantity, paid, type, stopType, Moment());
        account.Fills.Add(fill);
        account.Send(fill);
        if (account.FillTwice)
        {
            account.Send(fill);
        }

        account.Send(Snapshot(symbol, position));
    }

    private ExchangePosition Snapshot(string symbol, Position position)
    {
        return position.Size == 0
            ? ExchangePosition.Empty(symbol) with { Leverage = position.Leverage }
            : new ExchangePosition(symbol, position.Side, position.Size, position.Average, position.Stop, position.Target, position.Leverage, !Hedged(symbol),
                (_pairs[symbol].Price - position.Average) * position.Size * (int)position.Side, _pairs[symbol].Price);
    }

    private Account Read(ExchangeCredentials credentials)
    {
        Account account = Key(credentials.ApiKey);
        account.Refusal?.Invoke();
        if (account.Unavailable)
        {
            throw new ExchangeUnavailableException("Bybit is unavailable: no answer");
        }

        return account;
    }

    // A write of an account that does not answer gets no answer: the call is for now (review R8).
    private bool Hedged(string symbol)
    {
        return _accounts.Values.Any(account => account.Positions.TryGetValue(symbol, out _) && account.Hedge.Contains(symbol));
    }

    private Account? Write(ExchangeCredentials credentials)
    {
        Account account = Key(credentials.ApiKey);
        account.Refusal?.Invoke();
        return account.Unavailable ? null : account;
    }

    private static readonly ExchangeAnswer NoAnswer = new ExchangeAnswer(ExchangeAnswerKind.Temporary, null, "no answer");

    private static ExchangeAnswer Invalid(string reason)
    {
        return new ExchangeAnswer(ExchangeAnswerKind.Refused, "10001", reason);
    }

    private static bool OnStep(decimal value, decimal step)
    {
        return step <= 0 || value % step == 0;
    }

    // A stop or a target of a position of the side: on the step of the price and on its side of the price now, as Bybit asks.
    private static string? Level(PairRules rules, TradeSide side, decimal price, decimal? stop, decimal? target)
    {
        int sign = (int)side;
        if (stop is { } low && (!OnStep(low, rules.TickSize) || (price - low) * sign <= 0))
        {
            return $"StopLoss:{low} set for {side} position should be on its side of base_price:{price} and on the tick size";
        }

        return target is { } high && (!OnStep(high, rules.TickSize) || (high - price) * sign <= 0)
            ? $"TakeProfit:{high} set for {side} position should be on its side of base_price:{price} and on the tick size"
            : null;
    }

    private void Record(string call, string apiKey, string? symbol)
    {
        Calls.Add((call, apiKey, symbol));
    }

    private string NextId()
    {
        return $"exec-{Interlocked.Increment(ref _sequence)}";
    }

    private static TradeSide Opposite(TradeSide side)
    {
        return side == TradeSide.Long ? TradeSide.Short : TradeSide.Long;
    }

    private sealed record PairState(PairRules Rules, decimal Price)
    {
        public decimal Price { get; set; } = Price;
    }

    /// <summary>
    ///     A position of the fake: its side, size, mean price, stop, target and leverage.
    /// </summary>
    public sealed record Position(TradeSide Side, decimal Size, decimal Average, decimal? Stop, decimal? Target, int Leverage)
    {
        public TradeSide Side { get; set; } = Side;

        public decimal Size { get; set; } = Size;

        public decimal Average { get; set; } = Average;

        public decimal? Stop { get; set; } = Stop;

        public decimal? Target { get; set; } = Target;
    }

    /// <summary>
    ///     The account of a key and what the test makes it do.
    /// </summary>
    public sealed class Account(FakeBybit bybit, string apiKey)
    {
        private readonly Dictionary<string, Queue<ExchangeAnswer>> _script = new Dictionary<string, Queue<ExchangeAnswer>>(StringComparer.Ordinal);

        public string ApiKey { get; } = apiKey;

        public ExchangeKeyInfo KeyInfo { get; set; } = new ExchangeKeyInfo(104857321, false, true, ["AccountTransfer"], ["Order", "Position"]);

        public string? MarginMode { get; set; } = "ISOLATED_MARGIN";

        public decimal Free { get; set; } = 1250.40m;

        /// <summary>
        ///     Throws instead of every answer: a key Bybit does not take.
        /// </summary>
        public Action? Refusal { get; set; }

        /// <summary>
        ///     The reads do not answer.
        /// </summary>
        public bool Unavailable { get; set; }

        /// <summary>
        ///     While <see cref="Unavailable" />, the stream keeps silent too.
        /// </summary>
        public bool MuteWhenUnavailable { get; set; }

        /// <summary>
        ///     The stream of the key does not open, with this reason.
        /// </summary>
        public string? StreamRefusal { get; set; }

        /// <summary>
        ///     The share of the next close the book fills; the rest of it is cancelled.
        /// </summary>
        public decimal? PartialClose { get; set; }

        /// <summary>
        ///     The order of a close filled in part is told as filled.
        /// </summary>
        public bool PartialReportsFilled { get; set; }

        /// <summary>
        ///     The orders are not found by their number: the history of Bybit lags.
        /// </summary>
        public bool HistoryLags { get; set; }

        /// <summary>
        ///     The moments the fills were read from.
        /// </summary>
        public List<DateTimeOffset> FillsSince { get; } = [];

        /// <summary>
        ///     A fill goes before the update of its order.
        /// </summary>
        public bool FillBeforeOrder { get; set; }

        /// <summary>
        ///     A fill is sent twice by the stream.
        /// </summary>
        public bool FillTwice { get; set; }

        /// <summary>
        ///     How many of the next messages the live streams keep silent about.
        /// </summary>
        public int Silent { get; set; }

        public HashSet<string> Hedge { get; } = new HashSet<string>(StringComparer.Ordinal);

        internal Dictionary<string, Position> Positions { get; } = new Dictionary<string, Position>(StringComparer.Ordinal);

        internal Dictionary<string, int> Leverage { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        internal List<ExchangeFill> Fills { get; } = [];

        internal Dictionary<string, (ExchangeOrderRequest Request, ExchangeOrderUpdate Update)> Orders { get; } = new Dictionary<string, (ExchangeOrderRequest, ExchangeOrderUpdate)>(StringComparer.Ordinal);

        internal List<(string Symbol, ExchangeClosedResult Result)> Closed { get; } = [];

        internal List<AccountStream> Streams { get; } = [];

        /// <summary>
        ///     The next answers of a call — <c>set-leverage</c>, <c>order-create</c>, <c>trading-stop</c> — before it does what it
        ///     does; an answer with the code <c>lost</c> to <c>order-create</c> takes the order and loses the answer.
        /// </summary>
        public Account Answer(string call, params ExchangeAnswer[] answers)
        {
            lock (bybit._gate)
            {
                if (!_script.TryGetValue(call, out Queue<ExchangeAnswer>? queue))
                {
                    queue = new Queue<ExchangeAnswer>();
                    _script[call] = queue;
                }

                foreach (ExchangeAnswer answer in answers)
                {
                    queue.Enqueue(answer);
                }
            }

            return this;
        }

        /// <summary>
        ///     A position the owner holds already, not opened by the bench.
        /// </summary>
        public Account Holds(string symbol, TradeSide side, decimal size, decimal price)
        {
            lock (bybit._gate)
            {
                Positions[symbol] = new Position(side, size, price, null, null, 10);
            }

            return this;
        }

        /// <summary>
        ///     Whether a stream of the key is open and live.
        /// </summary>
        public bool Streaming
        {
            get
            {
                lock (bybit._gate)
                {
                    return Streams.Any(stream => stream is { Disposed: false, Live: true });
                }
            }
        }

        public int OpenStreams
        {
            get
            {
                lock (bybit._gate)
                {
                    return Streams.Count(stream => !stream.Disposed);
                }
            }
        }

        /// <summary>
        ///     The connection of the streams of the key is lost: what happens meanwhile is not sent.
        /// </summary>
        public void Drop()
        {
            lock (bybit._gate)
            {
                foreach (AccountStream stream in Streams.Where(stream => !stream.Disposed))
                {
                    stream.Live = false;
                    stream.OnState(ExchangeStreamState.Lost);
                }
            }
        }

        /// <summary>
        ///     The connection comes back.
        /// </summary>
        public void Restore()
        {
            lock (bybit._gate)
            {
                foreach (AccountStream stream in Streams.Where(stream => !stream.Disposed))
                {
                    stream.Live = true;
                    stream.OnState(ExchangeStreamState.Restored);
                }
            }
        }

        /// <summary>
        ///     The package gives up: the connection is closed for good.
        /// </summary>
        public void Close()
        {
            lock (bybit._gate)
            {
                foreach (AccountStream stream in Streams.Where(stream => !stream.Disposed))
                {
                    stream.Live = false;
                    stream.OnState(ExchangeStreamState.Closed);
                }
            }
        }

        internal ExchangeAnswer? Next(string call)
        {
            return _script.TryGetValue(call, out Queue<ExchangeAnswer>? queue) && queue.Count > 0 ? queue.Dequeue() : null;
        }

        internal void Send(ExchangeMessage message)
        {
            if (Unavailable && MuteWhenUnavailable)
            {
                return;
            }

            if (Silent > 0 && Streams.Any(stream => stream is { Disposed: false, Live: true }))
            {
                Silent--;
                return;
            }

            foreach (AccountStream stream in Streams.Where(stream => stream is { Disposed: false, Live: true }))
            {
                stream.OnMessage(message);
            }
        }
    }

    internal sealed class AccountStream(FakeBybit bybit, Account account, Action<ExchangeMessage> onMessage, Action<ExchangeStreamState> onState) : IExchangeSubscription
    {
        public Action<ExchangeMessage> OnMessage { get; } = onMessage;

        public Action<ExchangeStreamState> OnState { get; } = onState;

        public bool Live { get; set; } = true;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            lock (bybit._gate)
            {
                Disposed = true;
                account.Streams.Remove(this);
            }

            return ValueTask.CompletedTask;
        }
    }

    internal sealed class PriceStream(FakeBybit bybit, string symbol, Action<string, decimal> onMark, Action<ExchangeStreamState> onState) : IExchangeSubscription
    {
        public string Symbol { get; } = symbol;

        public Action<string, decimal> OnMark { get; } = onMark;

        public Action<ExchangeStreamState> OnState { get; } = onState;

        public bool Live { get; set; } = true;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            lock (bybit._gate)
            {
                Disposed = true;
                Live = false;
                bybit._prices.Remove(this);
            }

            return ValueTask.CompletedTask;
        }
    }
}
