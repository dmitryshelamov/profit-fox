using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;

namespace ProfitFox.Application.LiveTrading.Queries.GetBotPositions;

/// <summary>
///     A position of a live bot with its orders and their fills. <see cref="OpenedAt" /> is the time of its first fill; the prices
///     are averages of its own entry and exit fills, <c>null</c> without them. <see cref="ExitSide" /> is the side of its market
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