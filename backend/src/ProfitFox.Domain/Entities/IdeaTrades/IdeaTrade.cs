using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Entities.IdeaTrades;

/// <summary>
///     A trade by an idea: one entry with a stop and a target that stand on the exchange, the levels and the size it was entered with, and
///     what the exchange counted of its orders. <see cref="AutoId" /> is the auto that took the trade, none for a manual one; <see cref="Leverage" /> the leverage it entered with. <see cref="Entry" /> and the exits hold the quantity, value and fee of their orders;
///     <see cref="CancelRequested" /> and <see cref="CloseRequested" /> ask a Worker to cancel the entry or to close the position.
/// </summary>
public sealed record IdeaTrade(
    Guid Id,
    Guid IdeaId,
    Guid AccountId,
    Instrument Instrument,
    IdeaSetup Setup,
    Interval Interval,
    IdeaType Type,
    IdeaSide Side,
    decimal EntryPrice,
    decimal Stop,
    decimal Target,
    decimal Quantity,
    decimal Amount,
    decimal Risk,
    DateTimeOffset? EntryDeadline,
    IdeaTradeStatus Status,
    DateTimeOffset StatusAt,
    string? Reason,
    bool CancelRequested,
    bool CloseRequested,
    LegTotals Entry,
    LegTotals TakeProfit,
    LegTotals StopLoss,
    LegTotals Manual,
    int LastOrderNumber,
    DateTimeOffset CreatedAt,
    IdeaPromise? Promise = null,
    DateTimeOffset? OpenedAt = null,
    TradedIdeaOutcome? IdeaOutcome = null,
    Guid? AutoId = null,
    int Leverage = 1)
{
    /// <summary>The close at the expiry of the idea: only a paper trade has it, a real trade's is always none.</summary>
    public LegTotals Expiry { get; init; } = LegTotals.None;

    /// <summary>The features of the market its idea fired with, copied when the trade was made; a trade before them has none.</summary>
    public IdeaFeatures Features { get; init; }

    public bool IsOpen => Status is IdeaTradeStatus.AwaitingEntry or IdeaTradeStatus.Open;

    /// <summary>The quantity the entry filled that no exit has taken out yet.</summary>
    public decimal HeldQuantity => Entry.Quantity - Exited.Quantity;

    /// <summary>The money of a closed trade after the fees; none for a trade that is open, cancelled or failed.</summary>
    public decimal? Result => Status is IdeaTradeStatus.ClosedAtTarget or IdeaTradeStatus.ClosedAtStop or IdeaTradeStatus.ClosedManually or IdeaTradeStatus.ClosedAtExpiry
        ? (Side == IdeaSide.Long ? Exited.Value - Entry.Value : Entry.Value - Exited.Value) - Entry.Fee - Exited.Fee
        : null;

    /// <summary>The money of the account the trade takes on the exchange: its amount at its leverage.</summary>
    public decimal Margin => Amount / Leverage;

    /// <summary>The fees of the entry and of the exits.</summary>
    public decimal Fees => Entry.Fee + Exited.Fee;

    /// <summary>The time from the first fill of the entry to the close of the trade; none for a trade that never opened.</summary>
    public TimeSpan? TimeToOutcome => StatusAt - OpenedAt;

    /// <summary>The result in units of the risk the trade was entered with.</summary>
    public decimal? ResultR => Result / Risk;

    /// <summary>
    ///     What an open trade is worth now: the part the exits took out, plus the rest closed by the market at <paramref name="price" /> with the taker
    ///     fee of <paramref name="takerPercent" /> in percent, less the fee of the entry; none for a trade that does not hold anything yet or is done.
    /// </summary>
    public decimal? ResultAt(decimal price, decimal takerPercent)
    {
        if (Status != IdeaTradeStatus.Open)
        {
            return null;
        }

        decimal rest = HeldQuantity * price;
        decimal gross = Side == IdeaSide.Long ? Exited.Value + rest - Entry.Value : Entry.Value - Exited.Value - rest;

        return gross - Entry.Fee - Exited.Fee - (rest * takerPercent / 100m);
    }

    /// <summary>The result at the price in units of the risk the trade was entered with.</summary>
    public decimal? ResultRAt(decimal price, decimal takerPercent)
    {
        return ResultAt(price, takerPercent) / Risk;
    }

    private LegTotals Exited => new LegTotals(
        TakeProfit.Quantity + StopLoss.Quantity + Manual.Quantity + Expiry.Quantity,
        TakeProfit.Value + StopLoss.Value + Manual.Value + Expiry.Value,
        TakeProfit.Fee + StopLoss.Fee + Manual.Fee + Expiry.Fee);

    public static IdeaTrade Create(Guid id, Idea idea, Guid accountId, decimal entryPrice, IdeaTradeSize size, DateTimeOffset now, IdeaPromise? promise = null, Guid? autoId = null, int leverage = 1)
    {
        return new IdeaTrade(
            id,
            idea.Id,
            accountId,
            idea.Instrument,
            idea.Setup,
            idea.Interval,
            idea.Track.Plan.Type,
            idea.Track.Plan.Side,
            entryPrice,
            idea.Track.Plan.Stop,
            idea.Track.Plan.Target,
            size.Quantity,
            size.Amount,
            size.Risk,
            idea.Track.Plan.Type == IdeaType.Pullback ? IdeaTracker.EntryDeadline(idea.Track) : null,
            IdeaTradeStatus.AwaitingEntry,
            now,
            null,
            false,
            false,
            LegTotals.None,
            LegTotals.None,
            LegTotals.None,
            LegTotals.None,
            0,
            now,
            promise,
            AutoId: autoId,
            Leverage: idea.Instrument.Category == MarketCategory.Spot ? 1 : leverage)
        { Features = idea.Features };
    }

    /// <summary>A pullback that has not entered by its deadline gives its entry up.</summary>
    public bool EntryExpired(DateTimeOffset now)
    {
        return EntryDeadline <= now;
    }

    /// <summary>The totals the exchange counted of the entry; the first fill opens the trade, and one that the exits took out already closes it.</summary>
    public IdeaTrade EntryGrown(LegTotals totals, DateTimeOffset now)
    {
        IdeaTrade grown = this with { Entry = totals };

        if (Status != IdeaTradeStatus.AwaitingEntry || totals.Quantity <= 0m)
        {
            return grown;
        }

        IdeaTrade open = grown with { Status = IdeaTradeStatus.Open, StatusAt = now, OpenedAt = now };
        IdeaTradeLeg largest = new[] { IdeaTradeLeg.StopLoss, IdeaTradeLeg.TakeProfit, IdeaTradeLeg.Manual }.MaxBy(leg => open.ExitOf(leg).Quantity);

        return open.Settled(largest, now);
    }

    /// <summary>The entry is over, with no more to fill: a trade it filled nothing of is cancelled, one it filled in part stays open.</summary>
    public IdeaTrade EntryEnded(DateTimeOffset now)
    {
        return Status == IdeaTradeStatus.AwaitingEntry ? this with { Status = IdeaTradeStatus.Cancelled, StatusAt = now } : this;
    }

    /// <summary>
    ///     What an order of an exit took out, added to its leg: a leg may have several orders, such as a second market close. The exit that takes
    ///     out the last of the quantity closes the trade.
    /// </summary>
    public IdeaTrade ExitGrown(IdeaTradeLeg leg, LegTotals added, DateTimeOffset now)
    {
        IdeaTrade grown = leg switch
        {
            IdeaTradeLeg.TakeProfit => this with { TakeProfit = TakeProfit + added },
            IdeaTradeLeg.StopLoss => this with { StopLoss = StopLoss + added },
            IdeaTradeLeg.Expiry => this with { Expiry = Expiry + added },
            _ => this with { Manual = Manual + added }
        };

        return grown.Settled(leg, now);
    }

    private LegTotals ExitOf(IdeaTradeLeg leg)
    {
        return leg switch
        {
            IdeaTradeLeg.TakeProfit => TakeProfit,
            IdeaTradeLeg.StopLoss => StopLoss,
            _ => Manual
        };
    }

    private IdeaTrade Settled(IdeaTradeLeg leg, DateTimeOffset now)
    {
        if (Status != IdeaTradeStatus.Open || HeldQuantity > 0m)
        {
            return this;
        }

        IdeaTradeStatus closed = leg switch
        {
            IdeaTradeLeg.TakeProfit => IdeaTradeStatus.ClosedAtTarget,
            IdeaTradeLeg.StopLoss => IdeaTradeStatus.ClosedAtStop,
            IdeaTradeLeg.Expiry => IdeaTradeStatus.ClosedAtExpiry,
            _ => IdeaTradeStatus.ClosedManually
        };

        return this with { Status = closed, StatusAt = now };
    }

    /// <exception cref="IdeaTradeConflictException">The trade does not wait for its entry.</exception>
    public IdeaTrade RequestCancel()
    {
        return Status == IdeaTradeStatus.AwaitingEntry ? this with { CancelRequested = true } : throw new IdeaTradeConflictException("Only a trade that waits for its entry is cancelled.");
    }

    /// <exception cref="IdeaTradeConflictException">The trade is not open.</exception>
    public IdeaTrade RequestClose()
    {
        return Status == IdeaTradeStatus.Open ? this with { CloseRequested = true } : throw new IdeaTradeConflictException("Only an open trade is closed.");
    }

    /// <summary>The trade stops with the reason; the first reason stays, and a trade that is done does not fail. Its stop and target stay on the exchange.</summary>
    public IdeaTrade Fail(string reason, DateTimeOffset now)
    {
        return IsOpen ? this with { Status = IdeaTradeStatus.Failed, StatusAt = now, Reason = reason } : this;
    }
}