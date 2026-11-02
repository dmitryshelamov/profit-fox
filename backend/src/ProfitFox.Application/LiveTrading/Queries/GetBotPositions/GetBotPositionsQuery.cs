using MediatR;

namespace ProfitFox.Application.LiveTrading.Queries.GetBotPositions;

/// <summary>The positions of a bot in live trading, the newest first; <c>null</c> when there is no such bot.</summary>
public sealed record GetBotPositionsQuery(Guid Id) : IRequest<IReadOnlyList<LivePositionDetails>?>;