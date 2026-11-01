using MediatR;

using ProfitFox.Domain.Entities.Bots;

namespace ProfitFox.Application.Bots.Queries.GetBots;

public sealed record GetBotsQuery : IRequest<IReadOnlyList<Bot>>;