using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;

namespace ProfitFox.Application.LiveTrading.Queries.GetBotPositions;

/// <summary>
///     A position of a live bot with its orders and their trades. <see cref="OpenedAt" /> is the time of its first trade; the
///     prices are averages of the totals of its entry and exit orders, <c>null</c> before a fill. <see cref="ExitSide" /> is the side of its market
///     exit by the version of the strategy the bot trades.
/// </summary>
public sealed record LivePositionDetails(
    LivePosition Position,
    PositionStatus Status,
    OrderSide ExitSide,
    DateTimeOffset? OpenedAt,
    decimal? EntryPrice,
    decimal? ExitPrice,
    IReadOnlyList<LiveOrderDetails> Orders);

public sealed record LiveOrderDetails(LiveOrder Order, IReadOnlyList<LiveFill> Fills);