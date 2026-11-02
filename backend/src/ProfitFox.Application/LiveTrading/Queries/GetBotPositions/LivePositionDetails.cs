using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Services.Strategies.Execution;

namespace ProfitFox.Application.LiveTrading.Queries.GetBotPositions;

/// <summary>
///     A position of a live bot with its orders and their fills. <see cref="OpenedAt" /> is the time of its first fill; the prices
///     are averages of its own entry and exit fills, <c>null</c> without them.
/// </summary>
public sealed record LivePositionDetails(
    LivePosition Position,
    PositionStatus Status,
    DateTimeOffset? OpenedAt,
    decimal? EntryPrice,
    decimal? ExitPrice,
    IReadOnlyList<LiveOrderDetails> Orders);

public sealed record LiveOrderDetails(LiveOrder Order, IReadOnlyList<LiveFill> Fills);