using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Entities.Trades;

/// <summary>
///     A trade by an idea with real money (change take-idea-trades, design.md, decisions 10 and 12; spec idea-trades): the
///     idea it was entered by — kept as a snapshot, since closed ideas may be removed —, the account by its id and name, the
///     amount and the leverage, the price of the click, the quantity of the entry order, its stop with where it comes from,
///     its target and its end, and the wait for the entry and the attempts to put the stop back it got at the click. Then
///     where it is, and its numbers from its fills. A move its table of states does not have throws.
/// </summary>
public sealed class Trade
{
    private static readonly Dictionary<TradeState, TradeState[]> Moves = new Dictionary<TradeState, TradeState[]>
    {
        [TradeState.Pending] = [TradeState.Open, TradeState.Cancelled, TradeState.Error, TradeState.Closed],
        [TradeState.Open] = [TradeState.Closing, TradeState.Closed, TradeState.Error],
        [TradeState.Closing] = [TradeState.Open, TradeState.Closed, TradeState.Error],
        [TradeState.Error] = [TradeState.Closed],
        [TradeState.Closed] = [],
        [TradeState.Cancelled] = []
    };

    public required Guid Id { get; init; }

    public required Guid IdeaId { get; init; }

    public required string Symbol { get; init; }

    public required TradeSide Side { get; init; }

    public required Guid StrategyId { get; init; }

    public required string Strategy { get; init; }

    public required int Version { get; init; }

    public required TimeSpan Timeframe { get; init; }

    /// <summary>
    ///     The moment of the idea and its end: the end of the trade.
    /// </summary>
    public required DateTimeOffset IdeaMoment { get; init; }

    public required DateTimeOffset Until { get; init; }

    /// <summary>
    ///     The entry the idea showed at the click.
    /// </summary>
    public required decimal IdeaEntry { get; init; }

    /// <summary>
    ///     Null once the account was removed; its name stays.
    /// </summary>
    public Guid? AccountId { get; init; }

    public required string AccountName { get; init; }

    public required decimal Amount { get; init; }

    public required int Leverage { get; init; }

    public required decimal ClickPrice { get; init; }

    /// <summary>
    ///     The quantity of the entry order.
    /// </summary>
    public required decimal Quantity { get; init; }

    public required decimal Stop { get; init; }

    public required StopSource StopSource { get; init; }

    public decimal? Target { get; init; }

    public required TimeSpan EntryWait { get; init; }

    public required int StopAttempts { get; init; }

    public int StopFailures { get; set; }

    public TradeState State { get; private set; } = TradeState.Pending;

    /// <summary>
    ///     Why the trade was cancelled, is in error, or why the exchange refused its close.
    /// </summary>
    public string? Reason { get; set; }

    public TradeCloseReason? CloseReason { get; private set; }

    public TradeResult Numbers { get; private set; } = TradeResult.None;

    /// <summary>
    ///     The outcome of a closed trade in USDT; null for any other.
    /// </summary>
    public decimal? Result { get; private set; }

    /// <summary>
    ///     The result of the open position by the mark price and when it was counted.
    /// </summary>
    public decimal? Unrealised { get; set; }

    public DateTimeOffset? MarkedAt { get; set; }

    /// <summary>
    ///     Since when the exchange shows no position while the fills still hold some (design.md, decision 3).
    /// </summary>
    public DateTimeOffset? FlatSince { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    ///     A trade in a state that holds its pair on its account: «Ждёт входа», «Открыта», «Закрывается», «Ошибка».
    /// </summary>
    public bool Holds => IsHolding(State);

    public static bool IsHolding(TradeState state)
    {
        return state is TradeState.Pending or TradeState.Open or TradeState.Closing or TradeState.Error;
    }

    /// <summary>
    ///     The trade as the store keeps it.
    /// </summary>
    public Trade Restore(TradeState state, string? reason, TradeCloseReason? closeReason, TradeResult numbers, decimal? result, DateTimeOffset? openedAt, DateTimeOffset? closedAt)
    {
        State = state;
        Reason = reason;
        CloseReason = closeReason;
        Numbers = numbers;
        Result = result;
        OpenedAt = openedAt;
        ClosedAt = closedAt;
        return this;
    }

    /// <summary>
    ///     Takes the numbers of all the fills of the trade and moves it by them (design.md, decisions 5 and 10): the first fill
    ///     of the entry opens it; a fill that makes the position bigger and is not the entry puts it in error; the fill that
    ///     brings what is held to nought closes it with its reason. A trade in error or ended only takes the numbers.
    /// </summary>
    public void Apply(IReadOnlyCollection<TradeFill> fills)
    {
        var numbers = TradeResult.Of(Side, fills);
        Numbers = numbers;
        if (State is TradeState.Closed or TradeState.Cancelled or TradeState.Error)
        {
            return;
        }

        if (State == TradeState.Pending && numbers.Entered > 0)
        {
            Move(TradeState.Open);
            OpenedAt = fills.Where(fill => fill.Kind == FillKind.Entry).Min(fill => fill.Time);
            Reason = null;
        }

        if (numbers.Increased)
        {
            Fail("позиция на бирже больше сделки: её увеличили не из стенда");
            return;
        }

        if (State is TradeState.Open or TradeState.Closing && numbers.Entered > 0 && numbers.Held == 0 && numbers.LastExit is { } last)
        {
            Close(last.Reason ?? TradeCloseReason.Exchange, last.Time, numbers.Result);
        }
    }

    /// <summary>
    ///     A close order was written: the trade closes.
    /// </summary>
    public void StartClosing()
    {
        Move(TradeState.Closing);
    }

    /// <summary>
    ///     The exchange refused the close order: the trade stays open with the reason, and the close can be tried again.
    /// </summary>
    public void CloseRefused(string reason)
    {
        Move(TradeState.Open);
        Reason = reason;
    }

    /// <summary>
    ///     The entry did not happen.
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        Move(TradeState.Cancelled);
        Reason = reason;
        ClosedAt = at;
    }

    /// <summary>
    ///     The position on the exchange does not match the trade: the bench leads it no more.
    /// </summary>
    public void Fail(string reason)
    {
        Move(TradeState.Error);
        Reason = reason;
    }

    /// <summary>
    ///     The trade is closed with the reason and the outcome.
    /// </summary>
    public void Close(TradeCloseReason reason, DateTimeOffset at, decimal result)
    {
        Move(TradeState.Closed);
        CloseReason = reason;
        ClosedAt = at;
        Result = result;
        Unrealised = null;
        FlatSince = null;
    }

    private void Move(TradeState to)
    {
        if (!Moves[State].Contains(to))
        {
            throw new InvalidOperationException($"A trade does not move from {State} to {to}.");
        }

        State = to;
    }
}
