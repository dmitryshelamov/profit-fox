using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Application.Services.Trades;

/// <summary>
///     The trading Worker while it holds its lease (change take-idea-trades, design.md, decisions 2, 3, 4, 8, 10 and 11). One
///     loop takes, in their order, what the streams tell — they only put it in its queue — and every
///     <see cref="TradingSettings.Tick" /> reads the base: the orders to send, the trades past their end, the accounts whose
///     stream should open or close. A stream of an account opens with its first trade that holds a pair and closes
///     <see cref="TradingSettings.StreamIdle" /> after the last; the stream is the main path, REST reads the account after the
///     subscription — so nothing between them is lost —, after every break, every <see cref="TradingSettings.ControlInterval" />
///     while the stream lives, and every <see cref="TradingSettings.PollInterval" /> while there is none. The orders of an
///     account go once its stream opened and REST read it, or once it is polled. Without trades there are no connections and
///     no calls of the exchange. A write under a lost lease throws, and every stream is closed.
/// </summary>
public sealed class Trader(
    ITradeRepository trades,
    ITradeHaltRepository halt,
    ITradeSettingsRepository settings,
    IExchangeAccountRepository accounts,
    IExchangeTrading trading,
    IExchangeMarket market,
    IExchangeStream streams,
    IExchangePriceStream prices,
    ISecretProtector protector,
    TradingSettings options,
    TimeProvider time,
    ILogger<Trader> logger)
{
    /// <summary>
    ///     How far back a read of the fills goes before the cursor: the clocks of the exchange and of the bench may differ.
    /// </summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);

    private readonly Channel<Signal> _inbox = Channel.CreateUnbounded<Signal>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<Guid, Feed> _feeds = [];
    private readonly Dictionary<string, IExchangeSubscription> _priceStreams = new Dictionary<string, IExchangeSubscription>(StringComparer.Ordinal);
    private readonly Dictionary<string, decimal> _marks = new Dictionary<string, decimal>(StringComparer.Ordinal);
    private readonly TradeEventHandler _handler = new TradeEventHandler(trades, time, logger);
    private readonly Dictionary<Guid, DateTimeOffset> _unfilled = [];
    private readonly Dictionary<Guid, DateTimeOffset> _restChecked = [];
    private bool _pricesLive = true;
    private DateTimeOffset _pricesPolled = DateTimeOffset.MinValue;
    private DateTimeOffset _marked = DateTimeOffset.MinValue;
    private string _worker = "";

    /// <summary>
    ///     Leads the trades until <paramref name="cancellationToken" /> is cancelled or the lease is lost.
    /// </summary>
    /// <exception cref="LeaseLostException">A write found the lease of another Worker.</exception>
    public async Task RunAsync(string worker, CancellationToken cancellationToken)
    {
        _worker = worker;
        logger.LogInformation("Worker {Worker} leads the trades.", worker);
        try
        {
            DateTimeOffset next = time.GetUtcNow();
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan wait = next - time.GetUtcNow();
                if (wait > TimeSpan.Zero && !_inbox.Reader.TryPeek(out _))
                {
                    using var timeout = new CancellationTokenSource(wait, time);
                    using var both = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    try
                    {
                        await _inbox.Reader.WaitToReadAsync(both.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // The tick is due.
                    }
                }

                while (_inbox.Reader.TryRead(out Signal? signal))
                {
                    await Guard(() => TakeAsync(signal, cancellationToken), cancellationToken);
                }

                if (time.GetUtcNow() >= next)
                {
                    await Guard(() => TickAsync(cancellationToken), cancellationToken);
                    next = time.GetUtcNow() + options.Tick;
                }
            }
        }
        finally
        {
            await CloseAllAsync();
            logger.LogInformation("Worker {Worker} no longer leads the trades.", worker);
        }
    }

    // An error of a step is logged and the next goes on; a lost lease and the stop end the run.
    private async Task Guard(Func<Task> step, CancellationToken cancellationToken)
    {
        try
        {
            await step();
        }
        catch (Exception exception) when (exception is not LeaseLostException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "A step of the trading Worker failed.");
        }
    }

    // ------------------------------------------------------------------ the signals of the streams

    private async Task TakeAsync(Signal signal, CancellationToken cancellationToken)
    {
        switch (signal)
        {
            case Message message:
                if (_feeds.TryGetValue(message.AccountId, out Feed? feed) && feed.Box == message.Box)
                {
                    await HandleAsync(feed, message.Content, MessageSource.Stream, cancellationToken);
                }

                break;
            case StreamChanged changed:
                if (_feeds.TryGetValue(changed.AccountId, out Feed? lost) && lost.Box == changed.Box)
                {
                    await StreamStateAsync(lost, changed.State, cancellationToken);
                }

                break;
            case Mark mark:
                _marks[mark.Symbol] = mark.Price;
                break;
            case PricesChanged prices:
                _pricesLive = prices.State != ExchangeStreamState.Lost && prices.State != ExchangeStreamState.Closed;
                if (prices.State == ExchangeStreamState.Closed)
                {
                    await ClosePricesAsync();
                }

                break;
        }
    }

    private async Task HandleAsync(Feed feed, ExchangeMessage message, MessageSource source, CancellationToken cancellationToken)
    {
        IReadOnlyList<TradeBook> holding = await trades.HoldingAsync(cancellationToken);
        StopRequest? stop = await _handler.HandleAsync(_worker, feed.Account.Id, message, source, holding, cancellationToken);
        if (message is ExchangePosition position)
        {
            feed.Unrealised[position.Symbol] = position.Unrealised ?? 0m;
        }

        if (stop is not null)
        {
            ExchangeAnswer answer = await trading.SetStopAsync(feed.Credentials, stop.Symbol, stop.Stop, cancellationToken);
            await _handler.StopAnsweredAsync(_worker, stop, answer.Taken, answer.Reason, cancellationToken);
        }
    }

    private async Task StreamStateAsync(Feed feed, ExchangeStreamState state, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        switch (state)
        {
            case ExchangeStreamState.Lost:
                logger.LogWarning("The stream of account {Account} is lost: the account is polled.", feed.Account.Name);
                feed.StreamLive = false;
                await ModeAsync(feed, FeedMode.Poll, cancellationToken);
                feed.Polled = DateTimeOffset.MinValue;
                break;
            case ExchangeStreamState.Restored:
                logger.LogInformation("The stream of account {Account} is back: REST reads it once.", feed.Account.Name);
                feed.StreamLive = true;
                await SyncAsync(feed, cancellationToken);
                break;
            case ExchangeStreamState.Closed:
                logger.LogWarning("The stream of account {Account} is closed by the package: it opens anew, the account is polled meanwhile.", feed.Account.Name);
                feed.StreamLive = false;
                await DropStreamAsync(feed);
                await ModeAsync(feed, FeedMode.Poll, cancellationToken);
                feed.Polled = DateTimeOffset.MinValue;
                feed.ReopenAt = now + Backoff(feed.Reopens++);
                break;
        }
    }

    // ------------------------------------------------------------------ the tick

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        IReadOnlyList<TradeBook> holding = await trades.HoldingAsync(cancellationToken);
        // Each part on its own: a failure of one — Bybit or the base did not answer — does not stop the others.
        await Guard(() => FeedsAsync(holding, now, cancellationToken), cancellationToken);
        holding = await trades.HoldingAsync(cancellationToken);
        TradeHalt ban = await halt.GetAsync(cancellationToken);
        TradeSettings risk = await settings.GetAsync(cancellationToken);
        foreach (TradeBook book in holding)
        {
            if (book.Trade.AccountId is { } account && _feeds.TryGetValue(account, out Feed? feed) && feed.Mode != FeedMode.Opening)
            {
                await Guard(() => LeadAsync(book, feed, ban, risk, now, cancellationToken), cancellationToken);
            }
        }

        await Guard(() => PricesAsync(holding, now, cancellationToken), cancellationToken);
        await Guard(() => DayAsync(now, cancellationToken), cancellationToken);
    }

    // The streams and the polls of the accounts with trades that hold their pairs.
    private async Task FeedsAsync(IReadOnlyList<TradeBook> holding, DateTimeOffset now, CancellationToken cancellationToken)
    {
        HashSet<Guid> wanted = [.. holding.Select(book => book.Trade.AccountId).OfType<Guid>()];
        foreach (Guid id in wanted.Where(id => !_feeds.ContainsKey(id)))
        {
            if (await accounts.FindAsync(id, cancellationToken) is not { } account || await accounts.SecretAsync(id, cancellationToken) is not { } secret)
            {
                continue;
            }

            ExchangeCredentials credentials;
            try
            {
                credentials = new ExchangeCredentials(account.ApiKey, protector.Reveal(account.Id, secret));
            }
            catch (SecretUnreadableException exception)
            {
                logger.LogError(exception, "The secret of account {Account} does not decrypt: its trades are not led.", account.Name);
                continue;
            }

            var feed = new Feed(account, credentials) { Cursor = account.FillsCursorAt ?? holding.Where(book => book.Trade.AccountId == id).Min(book => book.Trade.CreatedAt) };
            _feeds[id] = feed;
            await Guard(() => OpenStreamAsync(feed, cancellationToken), cancellationToken);
        }

        foreach (Feed feed in _feeds.Values.ToList())
        {
            await Guard(() => FeedAsync(feed, wanted, now, cancellationToken), cancellationToken);
        }
    }

    // The stream or the poll of one account: its idle close, the reopening of its stream, its poll and its control.
    private async Task FeedAsync(Feed feed, HashSet<Guid> wanted, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (wanted.Contains(feed.Account.Id))
        {
            feed.IdleSince = null;
        }
        else
        {
            feed.IdleSince ??= now;
            if (now - feed.IdleSince >= options.StreamIdle)
            {
                logger.LogInformation("Account {Account} has no trades: its stream closes.", feed.Account.Name);
                await DropStreamAsync(feed);
                _feeds.Remove(feed.Account.Id);
                await accounts.SetFeedAsync(feed.Account.Id, null, now, _worker, cancellationToken);
                return;
            }
        }

        if (feed.Mode is FeedMode.Poll or FeedMode.Opening)
        {
            if (feed.Subscription is null && now >= feed.ReopenAt)
            {
                await OpenStreamAsync(feed, cancellationToken);
            }

            if (feed.Mode is FeedMode.Poll or FeedMode.Opening && now - feed.Polled >= options.PollInterval)
            {
                feed.Polled = now;
                await SyncAsync(feed, cancellationToken);
            }
        }
        else if (feed.Mode == FeedMode.Stream && now - feed.Reconciled >= options.ControlInterval)
        {
            await SyncAsync(feed, cancellationToken);
        }
    }

    private async Task OpenStreamAsync(Feed feed, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        FeedMode before = feed.Mode;
        feed.Mode = FeedMode.Opening;
        var subscription = new SubscriptionBox();
        using var wait = new CancellationTokenSource(options.StreamOpenWait, time);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wait.Token);
        try
        {
            Guid id = feed.Account.Id;
            subscription.Value = await streams.OpenAsync(
                feed.Credentials,
                message => _inbox.Writer.TryWrite(new Message(id, subscription, message)),
                state => _inbox.Writer.TryWrite(new StreamChanged(id, subscription, state)),
                both.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The stream of account {Account} did not open: the account is polled.", feed.Account.Name);
            feed.StreamLive = false;
            feed.Mode = before == FeedMode.Opening ? FeedMode.Poll : before;
            await ModeAsync(feed, FeedMode.Poll, cancellationToken);
            feed.ReopenAt = now + Backoff(feed.Reopens++);
            return;
        }

        feed.Box = subscription;
        feed.StreamLive = true;
        // The subscription goes before the read of REST: an event between them is not lost, and its repeat is dropped.
        await SyncAsync(feed, cancellationToken);
    }

    /// <summary>
    ///     Reads the account by REST and puts it on the stream when its stream lives, or on the poll. A read that fails leaves
    ///     the account on the poll, so the next round of the poll reads it again and its trades are led meanwhile; only a lost
    ///     lease goes up.
    /// </summary>
    private async Task SyncAsync(Feed feed, CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileAsync(feed, cancellationToken);
        }
        catch (Exception exception) when (exception is not LeaseLostException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "REST did not read account {Account}: it is polled.", feed.Account.Name);
            feed.Polled = time.GetUtcNow();
            if (feed.Mode != FeedMode.Poll)
            {
                await ModeAsync(feed, FeedMode.Poll, cancellationToken);
            }

            return;
        }

        feed.Reconciled = time.GetUtcNow();
        if (feed.StreamLive && feed.Subscription is not null)
        {
            if (feed.Mode != FeedMode.Stream)
            {
                await ModeAsync(feed, FeedMode.Stream, cancellationToken);
                logger.LogInformation("Account {Account} is on the stream.", feed.Account.Name);
            }

            feed.Reopens = 0;
        }
        else if (feed.Mode != FeedMode.Poll)
        {
            await ModeAsync(feed, FeedMode.Poll, cancellationToken);
        }
    }

    private async Task ModeAsync(Feed feed, FeedMode mode, CancellationToken cancellationToken)
    {
        // The mode in memory changes first: a failed write to the base does not leave the account unled.
        feed.Mode = mode;
        await accounts.SetFeedAsync(feed.Account.Id, mode == FeedMode.Stream ? "stream" : "poll", time.GetUtcNow(), _worker, cancellationToken);
    }

    /// <summary>
    ///     Reads the account by REST and gives what it read to the handler as the stream would: the fills from the cursor, then
    ///     the positions — of every trade that holds its pair, an empty one when the exchange has none —, then the orders sent
    ///     and not ended.
    /// </summary>
    private async Task ReconcileAsync(Feed feed, CancellationToken cancellationToken)
    {
        // From the cursor, but never before the earliest trade that holds a pair of the account: an old cursor would read a
        // window of Bybit that ends before the fills of now.
        DateTimeOffset? earliest = (await trades.HoldingAsync(cancellationToken)).Where(book => book.Trade.AccountId == feed.Account.Id).Select(book => (DateTimeOffset?)book.Trade.CreatedAt).Min();
        DateTimeOffset cursor = feed.Cursor ?? earliest ?? time.GetUtcNow();
        DateTimeOffset from = (earliest is { } first && first > cursor ? first : cursor) - Overlap;
        IReadOnlyList<ExchangeFill> fills = await trading.FillsAsync(feed.Credentials, from, cancellationToken);
        foreach (ExchangeFill fill in fills.OrderBy(fill => fill.Time))
        {
            await HandleAsync(feed, fill, MessageSource.Rest, cancellationToken);
        }

        if (fills.Count > 0 && fills.Max(fill => fill.Time) is var last && last > (feed.Cursor ?? DateTimeOffset.MinValue))
        {
            feed.Cursor = last;
            await accounts.SetFillsCursorAsync(feed.Account.Id, last, _worker, cancellationToken);
        }

        IReadOnlyList<ExchangePosition> positions = await trading.PositionsAsync(feed.Credentials, null, cancellationToken);
        IReadOnlyList<TradeBook> holding = [.. (await trades.HoldingAsync(cancellationToken)).Where(book => book.Trade.AccountId == feed.Account.Id)];
        foreach (TradeBook book in holding.Where(book => book.Trade.State is TradeState.Open or TradeState.Closing))
        {
            ExchangePosition position = positions.FirstOrDefault(found => found.Symbol == book.Trade.Symbol && found.OneWay) ?? ExchangePosition.Empty(book.Trade.Symbol);
            await HandleAsync(feed, position, MessageSource.Rest, cancellationToken);
        }

        foreach (TradeBook book in holding)
        {
            foreach (TradeOrder order in book.Orders.Where(order => order.Status == TradeOrderStatus.Sent))
            {
                if (await trading.OrderAsync(feed.Credentials, book.Trade.Symbol, order.LinkId, cancellationToken) is { } update)
                {
                    await HandleAsync(feed, update, MessageSource.Rest, cancellationToken);
                }
            }
        }
    }

    // ------------------------------------------------------------------ a trade

    private async Task LeadAsync(TradeBook book, Feed feed, TradeHalt ban, TradeSettings risk, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Trade trade = book.Trade;
        switch (trade.State)
        {
            case TradeState.Pending:
                await EntryAsync(book, feed, ban, risk, now, cancellationToken);
                break;
            case TradeState.Open:
                TradeCloseReason? close = ban.Halted ? TradeCloseReason.Halt : trade.Until <= now ? TradeCloseReason.Time : null;
                if (close is { } reason)
                {
                    await trades.ChangeAsync(trade.Id, _worker, changed => TradeEventHandler.AddClose(changed, reason, now), null, cancellationToken);
                    logger.LogInformation("Trade {Trade} on {Symbol} closes: {Reason}.", trade.Id, trade.Symbol, reason);
                }

                await FlatAsync(book, feed, now, cancellationToken);
                break;
            case TradeState.Closing:
                if (book.Orders.FirstOrDefault(order => order.Purpose == TradeOrderPurpose.Close && order.Status == TradeOrderStatus.Pending) is { } pending && pending.NextAttemptAt <= now)
                {
                    await CloseAsync(book, pending, feed, now, cancellationToken);
                }
                else if (trade.Numbers.Held > 0 && !book.Orders.Any(order => order.Purpose == TradeOrderPurpose.Close && order.Status is TradeOrderStatus.Pending or TradeOrderStatus.Sent))
                {
                    await RestAsync(book, feed, now, cancellationToken);
                }

                await FlatAsync(book, feed, now, cancellationToken);
                break;
        }
    }

    private async Task EntryAsync(TradeBook book, Feed feed, TradeHalt ban, TradeSettings risk, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Trade trade = book.Trade;
        if (book.Orders.FirstOrDefault(order => order.Number == 1) is not { } entry)
        {
            return;
        }

        bool late = now - trade.CreatedAt >= trade.EntryWait;
        if (entry.Status == TradeOrderStatus.Pending)
        {
            // An entry that has not gone is given up under the ban and past its wait («Вход не ждёт»); before each send the
            // price and the end are read anew, and an entry that no longer fits the risk is given up too.
            string? reason = ban.Halted ? "стоп-кран" : late ? "вход не ушёл вовремя" : null;
            if (reason is null && entry.NextAttemptAt > now)
            {
                return;
            }

            reason ??= await RefusalAsync(trade, risk, now, cancellationToken);
            if (reason is not null)
            {
                await GiveUpAsync(book, entry, feed, reason, now, cancellationToken);
                return;
            }

            await SendEntryAsync(book, entry, feed, now, cancellationToken);
            return;
        }

        if (entry.Status == TradeOrderStatus.Sent && late && trade.Numbers.Entered == 0)
        {
            // Taken and not filled yet: the exchange is asked about the order and the fills on every tick, and past CloseWait
            // without fills the trade opens by the snapshot of its position, so it is not left unled.
            DateTimeOffset since = _unfilled.TryGetValue(trade.Id, out DateTimeOffset seen) ? seen : _unfilled[trade.Id] = now;
            if (await trading.OrderAsync(feed.Credentials, trade.Symbol, entry.LinkId, cancellationToken) is { } update)
            {
                await HandleAsync(feed, update, MessageSource.Rest, cancellationToken);
            }

            await ReconcileAsync(feed, cancellationToken);
            if (now - since >= options.CloseWait && await trades.FindAsync(trade.Id, cancellationToken) is { State: TradeState.Pending, Numbers.Entered: 0 }
                                                 && (await trading.PositionsAsync(feed.Credentials, trade.Symbol, cancellationToken)).FirstOrDefault(position => position.OneWay && position.Size > 0 && position.Side == trade.Side) is { AveragePrice: { } average } held)
            {
                await trades.ChangeAsync(trade.Id, _worker, changed =>
                {
                    if (changed.Trade.State != TradeState.Pending || changed.Trade.Numbers.Entered > 0)
                    {
                        return false;
                    }

                    changed.Fills.Add(new TradeFill(TradeResult.SnapshotId(trade.Id), trade.Id, trade.Side, average, held.Size, 0m, FillKind.Entry, null, now));
                    changed.Trade.Apply(changed.Fills);
                    changed.Put(changed.Orders.Single(order => order.Number == 1) with { Status = TradeOrderStatus.Filled });
                    return true;
                }, null, cancellationToken);
                logger.LogWarning("Trade {Trade} on {Symbol} opens by the snapshot of its position ({Size} at {Average}): its fills did not come within {Wait}.", trade.Id, trade.Symbol, held.Size, average, options.CloseWait);
            }
        }
        else
        {
            _unfilled.Remove(trade.Id);
        }
    }

    /// <summary>
    ///     A trade that closes with no close order going and something held (review of the application, findings 3 and N3):
    ///     a close the exchange cancelled with a part filled gets a close of the rest at once; one told filled while the trade
    ///     still holds gets it when the position of the pair, read by REST, is still there.
    /// </summary>
    private async Task RestAsync(TradeBook book, Feed feed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Trade trade = book.Trade;
        TradeOrder? last = book.Orders.Where(order => order.Purpose == TradeOrderPurpose.Close).MaxBy(order => order.Number);
        bool alive = false;
        if (last is { Status: TradeOrderStatus.Filled } && now - _restChecked.GetValueOrDefault(trade.Id) >= options.PollInterval)
        {
            _restChecked[trade.Id] = now;
            alive = (await trading.PositionsAsync(feed.Credentials, trade.Symbol, cancellationToken)).Any(position => position.OneWay && position.Size > 0 && position.Side == trade.Side);
        }

        if (last is { Status: TradeOrderStatus.Cancelled } || alive)
        {
            await trades.ChangeAsync(trade.Id, _worker, changed => TradeEventHandler.AddRest(changed, now, alive), null, cancellationToken);
        }
    }

    /// <summary>
    ///     Why an entry about to be sent no longer goes, by the price of Bybit now and the settings of the risk module now: the
    ///     end of the idea is nearer than the margin, the price passed the stop or the target of the trade, the price went from
    ///     the price of the click in the way of the trade by more than <see cref="TradingSettings.EntryDrift" /> — the limit of
    ///     the loss is counted from the price of the click (design.md, decision 8), so the loss at the stop stays within the
    ///     limit plus the drift —, or the stop is not nearer than half the way to the liquidation from the price now. Null when
    ///     it goes.
    /// </summary>
    private async Task<string?> RefusalAsync(Trade trade, TradeSettings risk, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (trade.Until - now < risk.ExpiryMargin)
        {
            return "до срока идеи меньше запаса";
        }

        if (await market.PriceAsync(trade.Symbol, cancellationToken) is not { } price || price <= 0)
        {
            return "цена пары неизвестна";
        }

        int sign = (int)trade.Side;
        if ((price - trade.Stop) * sign <= 0)
        {
            return $"цена {TradeTexts.Price(price)} прошла стоп сделки {TradeTexts.Price(trade.Stop)}";
        }

        if (trade.Target is { } target && (price - target) * sign >= 0)
        {
            return $"цена {TradeTexts.Price(price)} прошла цель сделки {TradeTexts.Price(target)}";
        }

        decimal drift = (price - trade.ClickPrice) * sign / trade.ClickPrice;
        if (drift > options.EntryDrift)
        {
            return $"цена {TradeTexts.Price(price)} ушла от цены клика {TradeTexts.Price(trade.ClickPrice)} в сторону сделки на {TradeTexts.Price(decimal.Round(drift * 100, 2))} % — больше допуска {TradeTexts.Price(options.EntryDrift * 100)} %";
        }

        decimal distance = Math.Abs(price - trade.Stop) / price;
        return distance >= 1m / (2 * trade.Leverage) ? $"стоп {TradeTexts.Price(trade.Stop)} от цены {TradeTexts.Price(price)} не ближе половины пути до ликвидации" : null;
    }

    /// <summary>
    ///     Gives an entry up with the reason. An entry that was tried may be at the exchange though its answer was lost: the
    ///     exchange is asked first — the order by its number, the fills of the order and the position of the pair, as the
    ///     history of orders may lag — and an entry found there is sent, its trade opens by its fills and is led on.
    /// </summary>
    private async Task GiveUpAsync(TradeBook book, TradeOrder entry, Feed feed, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Trade trade = book.Trade;
        if (entry.Attempts > 0)
        {
            ExchangeOrderUpdate? found = await trading.OrderAsync(feed.Credentials, trade.Symbol, entry.LinkId, cancellationToken);
            bool filled = (await trading.FillsAsync(feed.Credentials, trade.CreatedAt - Overlap, cancellationToken)).Any(fill => fill.OrderLinkId == entry.LinkId);
            bool held = (await trading.PositionsAsync(feed.Credentials, trade.Symbol, cancellationToken)).Any(position => position.OneWay && position.Size > 0 && position.Side == trade.Side);
            if (found is not null || filled || held)
            {
                logger.LogWarning("The entry {Order} of trade {Trade} was at Bybit after all ({Reason} not applied): the trade is led on.", entry.LinkId, trade.Id, reason);
                await trades.ChangeAsync(trade.Id, _worker, changed =>
                {
                    changed.Put(changed.Orders.Single(order => order.Number == 1) with { Status = TradeOrderStatus.Sent, ExchangeOrderId = found?.OrderId });
                    return true;
                }, null, cancellationToken);
                if (found is not null)
                {
                    await HandleAsync(feed, found, MessageSource.Rest, cancellationToken);
                }

                await ReconcileAsync(feed, cancellationToken);
                return;
            }
        }

        await trades.ChangeAsync(trade.Id, _worker, changed =>
        {
            if (changed.Trade.State != TradeState.Pending)
            {
                return false;
            }

            changed.Put(changed.Orders.Single(order => order.Number == 1) with { Status = TradeOrderStatus.Cancelled });
            changed.Trade.Cancel(reason == "вход не ушёл вовремя" || reason == "стоп-кран" ? reason : $"вход отменён: {reason}", now);
            return true;
        }, null, cancellationToken);
        logger.LogWarning("Trade {Trade} on {Symbol} is cancelled: {Reason}.", trade.Id, trade.Symbol, reason);
    }

    private async Task SendEntryAsync(TradeBook book, TradeOrder entry, Feed feed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Trade trade = book.Trade;
        ExchangeAnswer leverage = await trading.SetLeverageAsync(feed.Credentials, trade.Symbol, trade.Leverage, cancellationToken);
        if (!leverage.Taken)
        {
            await AnsweredAsync(trade, entry, leverage, now, "плечо", cancellationToken);
            return;
        }

        // The attempt is written before the send: an entry that may have gone is never cancelled without asking the exchange.
        int attempts = entry.Attempts + 1;
        await trades.ChangeAsync(trade.Id, _worker, changed =>
        {
            changed.Put(changed.Orders.Single(order => order.Number == entry.Number) with { Attempts = attempts, NextAttemptAt = now + Backoff(attempts) });
            return true;
        }, null, cancellationToken);
        var order = new ExchangeOrderRequest(trade.Symbol, trade.Side, entry.Quantity, entry.LinkId, trade.Stop, trade.Target, false);
        logger.LogInformation("Trade {Trade}: the entry {Order} goes to Bybit, {Side} {Quantity} {Symbol}, stop {Stop}, target {Target}.", trade.Id, entry.LinkId, trade.Side, entry.Quantity, trade.Symbol, trade.Stop, trade.Target);
        ExchangeAnswer answer = await trading.PlaceOrderAsync(feed.Credentials, order, cancellationToken);
        await AnsweredAsync(trade, entry, answer, now, null, cancellationToken, counted: true);
        if (answer.Kind == ExchangeAnswerKind.Duplicate)
        {
            // Taken before, its answer lost: its fills are read now rather than at the end of the wait.
            await ReconcileAsync(feed, cancellationToken);
        }
    }

    private async Task CloseAsync(TradeBook book, TradeOrder close, Feed feed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Trade trade = book.Trade;
        decimal quantity = trade.Numbers.Held > 0 ? trade.Numbers.Held : close.Quantity;
        var order = new ExchangeOrderRequest(trade.Symbol, trade.Side == TradeSide.Long ? TradeSide.Short : TradeSide.Long, quantity, close.LinkId, null, null, true);
        logger.LogInformation("Trade {Trade}: the close {Order} goes to Bybit ({Reason}), {Quantity} {Symbol}.", trade.Id, close.LinkId, close.Reason, quantity, trade.Symbol);
        ExchangeAnswer answer = await trading.PlaceOrderAsync(feed.Credentials, order, cancellationToken);
        await AnsweredAsync(trade, close, answer, now, null, cancellationToken);
    }

    // An answer of the table of design.md, decision 4.
    // An attempt written before the send (counted) is not counted again.
    private async Task AnsweredAsync(Trade trade, TradeOrder order, ExchangeAnswer answer, DateTimeOffset now, string? step, CancellationToken cancellationToken, bool counted = false)
    {
        await trades.ChangeAsync(trade.Id, _worker, book =>
        {
            if (book.Orders.FirstOrDefault(found => found.Number == order.Number) is not { } current)
            {
                return false;
            }

            int attempts = counted ? current.Attempts : current.Attempts + 1;
            switch (answer.Kind)
            {
                case ExchangeAnswerKind.Accepted or ExchangeAnswerKind.Duplicate when current.Status == TradeOrderStatus.Pending:
                    book.Put(current with { Status = TradeOrderStatus.Sent, ExchangeOrderId = answer.OrderId ?? current.ExchangeOrderId, Attempts = attempts, Refusal = null });
                    return true;
                case ExchangeAnswerKind.Temporary or ExchangeAnswerKind.Unchanged:
                    book.Put(current with { Attempts = attempts, NextAttemptAt = now + Backoff(attempts), Refusal = answer.Reason });
                    logger.LogWarning("Bybit did not take {Order} of trade {Trade} for now ({Attempts}): {Reason}", current.LinkId, trade.Id, attempts, answer.Reason);
                    return true;
                case ExchangeAnswerKind.Refused:
                    string reason = step is null ? $"Bybit: {answer.Reason}" : $"Bybit не принял {step}: {answer.Reason}";
                    book.Put(current with { Status = TradeOrderStatus.Rejected, Attempts = attempts, Refusal = reason });
                    if (current.Purpose == TradeOrderPurpose.Entry && book.Trade.State == TradeState.Pending)
                    {
                        book.Trade.Cancel(reason, now);
                    }
                    else if (current.Purpose == TradeOrderPurpose.Close && book.Trade.State == TradeState.Closing)
                    {
                        book.Trade.CloseRefused(reason);
                    }

                    logger.LogWarning("Bybit refused {Order} of trade {Trade}: {Reason}", current.LinkId, trade.Id, reason);
                    return true;
                default:
                    return false;
            }
        }, null, cancellationToken);
    }

    // An empty position without its closing fills for longer than CloseWait: the fills are read again, and without them the
    // trade closes by the result Bybit counted, «на бирже».
    private async Task FlatAsync(TradeBook book, Feed feed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Trade trade = book.Trade;
        if (trade.FlatSince is not { } flat || now - flat < options.CloseWait)
        {
            return;
        }

        IReadOnlyList<ExchangeFill> fills = await trading.FillsAsync(feed.Credentials, trade.CreatedAt - Overlap, cancellationToken);
        foreach (ExchangeFill fill in fills.Where(fill => fill.Symbol == trade.Symbol).OrderBy(fill => fill.Time))
        {
            await HandleAsync(feed, fill, MessageSource.Rest, cancellationToken);
        }

        if (await trades.FindAsync(trade.Id, cancellationToken) is not { State: TradeState.Open or TradeState.Closing, FlatSince: not null })
        {
            return;
        }

        // One empty snapshot is not enough: the position of the pair is read again, and a position still there stays led.
        if ((await trading.PositionsAsync(feed.Credentials, trade.Symbol, cancellationToken)).Any(position => position.OneWay && position.Size > 0))
        {
            logger.LogWarning("Trade {Trade} on {Symbol}: the exchange showed no position, and REST shows one: the trade is led on.", trade.Id, trade.Symbol);
            await trades.ChangeAsync(trade.Id, _worker, changed =>
            {
                changed.Trade.FlatSince = null;
                return true;
            }, null, cancellationToken);
            return;
        }

        ExchangeClosedResult? closed = await trading.ClosedAsync(feed.Credentials, trade.Symbol, trade.OpenedAt ?? trade.CreatedAt, cancellationToken);
        await trades.ChangeAsync(trade.Id, _worker, changed =>
        {
            if (changed.Trade.State is not (TradeState.Open or TradeState.Closing))
            {
                return false;
            }

            changed.Trade.Close(TradeCloseReason.Exchange, closed?.ClosedAt ?? now, closed?.Result ?? changed.Trade.Numbers.Result);
            foreach (TradeOrder order in changed.Orders.Where(order => order.Status is TradeOrderStatus.Pending or TradeOrderStatus.Sent))
            {
                changed.Put(order with { Status = TradeOrderStatus.Cancelled });
            }

            return true;
        }, null, cancellationToken);
        logger.LogWarning("Trade {Trade} on {Symbol} closed without its fills, by the closed result of Bybit: {Result} USDT", trade.Id, trade.Symbol, closed?.Result);
    }

    // ------------------------------------------------------------------ the prices and the loss of the day

    private async Task PricesAsync(IReadOnlyList<TradeBook> holding, DateTimeOffset now, CancellationToken cancellationToken)
    {
        HashSet<string> wanted = [.. holding.Where(book => book.Trade.State is TradeState.Open or TradeState.Closing or TradeState.Error).Select(book => book.Trade.Symbol)];
        foreach (string symbol in _priceStreams.Keys.Where(symbol => !wanted.Contains(symbol)).ToList())
        {
            await _priceStreams[symbol].DisposeAsync();
            _priceStreams.Remove(symbol);
            _marks.Remove(symbol);
        }

        foreach (string symbol in wanted.Where(symbol => !_priceStreams.ContainsKey(symbol)))
        {
            try
            {
                _priceStreams[symbol] = await prices.OpenAsync(
                    symbol,
                    (pair, price) => _inbox.Writer.TryWrite(new Mark(pair, price)),
                    state => _inbox.Writer.TryWrite(new PricesChanged(state)),
                    cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "The stream of the prices of {Symbol} did not open: the prices are read by REST.", symbol);
                _pricesLive = false;
            }
        }

        if (wanted.Count > 0 && (!_pricesLive || wanted.Any(symbol => !_marks.ContainsKey(symbol))) && now - _pricesPolled >= options.PollInterval)
        {
            _pricesPolled = now;
            foreach ((string symbol, decimal price) in await market.MarksAsync(wanted, cancellationToken))
            {
                _marks[symbol] = price;
            }
        }
    }

    private async Task DayAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        IReadOnlyList<TradeBook> holding = await trades.HoldingAsync(cancellationToken);
        var unrealised = new Dictionary<Guid, decimal>();
        foreach (TradeBook book in holding.Where(book => book.Trade.Numbers.Held > 0))
        {
            Trade trade = book.Trade;
            Feed? feed = trade.AccountId is { } id ? _feeds.GetValueOrDefault(id) : null;
            if (feed is { Mode: FeedMode.Poll } && feed.Unrealised.TryGetValue(trade.Symbol, out decimal polled))
            {
                unrealised[trade.Id] = polled;
            }
            else if (_marks.TryGetValue(trade.Symbol, out decimal mark))
            {
                unrealised[trade.Id] = trade.Numbers.Unrealised(trade.Side, mark);
            }
            else if (feed is not null && feed.Unrealised.TryGetValue(trade.Symbol, out decimal told))
            {
                // No mark price: the result of the position Bybit told last.
                unrealised[trade.Id] = told;
            }
        }

        // A trade still without any price counts with the result written last; one never counted counts nought.
        IEnumerable<decimal> counted = unrealised.Values.Concat(holding.Where(book => book.Trade.Numbers.Held > 0 && !unrealised.ContainsKey(book.Trade.Id)).Select(book => book.Trade.Unrealised ?? 0m));

        TradeSettings risk = await settings.GetAsync(cancellationToken);
        TradeHalt ban = await halt.GetAsync(cancellationToken);
        if (unrealised.Count > 0 && now - _marked >= options.MarkInterval)
        {
            _marked = now;
            await trades.MarkAsync(_worker, unrealised, now, cancellationToken);
        }

        if (ban.Halted || risk.MaxDayLoss is not { } limit)
        {
            return;
        }

        (IReadOnlyList<(DateTimeOffset ClosedAt, decimal Result)> closed, _) = await trades.DayAsync(DayLoss.Start(now, ban.LiftedAt), cancellationToken);
        var day = DayLoss.Count(closed, counted, now, ban.LiftedAt);
        if (day.Loss >= limit && await halt.HaltAsync(_worker, decimal.Round(day.Loss, 2), limit, now, cancellationToken))
        {
            logger.LogWarning("The emergency stop went off: the loss of the day {Loss} USDT reached {Limit} USDT. Every trade closes by the market.", decimal.Round(day.Loss, 2), limit);
        }
    }

    // ------------------------------------------------------------------ the end

    private TimeSpan Backoff(int attempt)
    {
        double seconds = Math.Pow(2, Math.Min(attempt, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, options.MaxBackoff.TotalSeconds));
    }

    private static async Task DropStreamAsync(Feed feed)
    {
        if (feed.Subscription is { } subscription)
        {
            feed.Box = null;
            await subscription.DisposeAsync();
        }
    }

    private async Task ClosePricesAsync()
    {
        foreach (IExchangeSubscription subscription in _priceStreams.Values)
        {
            await subscription.DisposeAsync();
        }

        _priceStreams.Clear();
    }

    private async Task CloseAllAsync()
    {
        foreach (Feed feed in _feeds.Values)
        {
            try
            {
                await DropStreamAsync(feed);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The stream of account {Account} did not close well.", feed.Account.Name);
            }
        }

        _feeds.Clear();
        try
        {
            await ClosePricesAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The streams of the prices did not close well.");
        }
    }

    private enum FeedMode
    {
        Opening,
        Stream,
        Poll
    }

    /// <summary>
    ///     An account the trader leads: its key — the secret stays in memory while the account has trades —, its stream, how it
    ///     is read now, the cursor of its fills, the results of its positions from the last poll, and the times of its reads.
    /// </summary>
    private sealed class Feed(ExchangeAccount account, ExchangeCredentials credentials)
    {
        public ExchangeAccount Account { get; } = account;

        public ExchangeCredentials Credentials { get; } = credentials;

        public SubscriptionBox? Box { get; set; }

        public IExchangeSubscription? Subscription => Box?.Value;

        public FeedMode Mode { get; set; } = FeedMode.Opening;

        /// <summary>
        ///     Whether the stream is open and its connection lives.
        /// </summary>
        public bool StreamLive { get; set; }

        public DateTimeOffset? Cursor { get; set; }

        public Dictionary<string, decimal> Unrealised { get; } = new Dictionary<string, decimal>(StringComparer.Ordinal);

        public DateTimeOffset Polled { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset Reconciled { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset Checked { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset ReopenAt { get; set; } = DateTimeOffset.MinValue;

        public int Reopens { get; set; }

        public DateTimeOffset? IdleSince { get; set; }
    }

    // The stream a message belongs to, known to the handlers before OpenAsync returns; a message of an old stream is dropped.
    private sealed class SubscriptionBox
    {
        public IExchangeSubscription? Value { get; set; }
    }

    private abstract record Signal;

    private sealed record Message(Guid AccountId, SubscriptionBox Box, ExchangeMessage Content) : Signal;

    private sealed record StreamChanged(Guid AccountId, SubscriptionBox Box, ExchangeStreamState State) : Signal;

    private sealed record Mark(string Symbol, decimal Price) : Signal;

    private sealed record PricesChanged(ExchangeStreamState State) : Signal;
}
