using MediatR;

namespace ProfitFox.Application.Bots.Queries.GetBots;

public sealed record GetBotsQuery : IRequest<IReadOnlyList<BotListItem>>;