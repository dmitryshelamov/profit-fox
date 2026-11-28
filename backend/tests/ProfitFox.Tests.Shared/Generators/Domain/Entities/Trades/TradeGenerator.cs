using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Shared.Generators.Domain.Entities.Trades;

/// <summary>
///     Trades for the tests (change take-idea-trades): a long on <c>SOLUSDT</c> of 100 USDT at 3× entered at 237.40, 0.4 of
///     it, with the stop of the idea at 231.38 and the target at 245.54, waiting for its entry, unless the test gives what it
///     checks.
/// </summary>
public static class TradeGenerator
{
    public static readonly DateTimeOffset Click = new DateTimeOffset(2026, 11, 28, 14, 0, 0, TimeSpan.Zero);

    public static Trade Long(
        Guid? account = null,
        string accountName = "Основной",
        string symbol = "SOLUSDT",
        TradeSide side = TradeSide.Long,
        decimal amount = 100m,
        decimal quantity = 0.4m,
        decimal stop = 231.38m,
        decimal? target = 245.54m,
        DateTimeOffset? until = null,
        DateTimeOffset? created = null,
        Guid? idea = null,
        int stopAttempts = 3,
        TimeSpan? entryWait = null)
    {
        DateTimeOffset at = created ?? Click;
        return new Trade
        {
            Id = Guid.NewGuid(),
            IdeaId = idea ?? Guid.NewGuid(),
            Symbol = symbol,
            Side = side,
            StrategyId = Guid.NewGuid(),
            Strategy = "breakout-long",
            Version = 7,
            Timeframe = TimeSpan.FromHours(1),
            IdeaMoment = at.AddHours(-1),
            Until = until ?? at.AddHours(3),
            IdeaEntry = 236.10m,
            AccountId = account ?? Guid.NewGuid(),
            AccountName = accountName,
            Amount = amount,
            Leverage = 3,
            ClickPrice = 237.40m,
            Quantity = quantity,
            Stop = stop,
            StopSource = StopSource.Idea,
            Target = target,
            EntryWait = entryWait ?? TimeSpan.FromSeconds(30),
            StopAttempts = stopAttempts,
            CreatedAt = at
        };
    }

    /// <summary>
    ///     A trade by <paramref name="idea" />: its pair, side, strategy and end.
    /// </summary>
    public static Trade Of(Idea idea, Guid account, string accountName = "Основной", decimal quantity = 0.4m, decimal stop = 231.38m, decimal? target = 245.54m, DateTimeOffset? created = null)
    {
        DateTimeOffset at = created ?? DateTimeOffset.UtcNow;
        return new Trade
        {
            Id = Guid.NewGuid(),
            IdeaId = idea.Id,
            Symbol = idea.Symbol,
            Side = idea.Side == EntrySide.Long ? TradeSide.Long : TradeSide.Short,
            StrategyId = idea.StrategyId,
            Strategy = idea.StrategyName,
            Version = idea.StrategyVersion,
            Timeframe = idea.Timeframe,
            IdeaMoment = idea.Moment,
            Until = idea.Until,
            IdeaEntry = (decimal)idea.ShownEntry,
            AccountId = account,
            AccountName = accountName,
            Amount = 100m,
            Leverage = 3,
            ClickPrice = 237.40m,
            Quantity = quantity,
            Stop = stop,
            StopSource = StopSource.Idea,
            Target = target,
            EntryWait = TimeSpan.FromSeconds(30),
            StopAttempts = 3,
            CreatedAt = at.AddTicks(-(at.Ticks % TimeSpan.TicksPerMicrosecond))
        };
    }

    public static TradeOrder Order(Trade trade, int number, TradeOrderPurpose purpose, TradeCloseReason? reason = null, TradeOrderStatus status = TradeOrderStatus.Pending)
    {
        return new TradeOrder(trade.Id, number, purpose, reason, trade.Quantity, status, 0, trade.CreatedAt, null, null, trade.CreatedAt);
    }
}
