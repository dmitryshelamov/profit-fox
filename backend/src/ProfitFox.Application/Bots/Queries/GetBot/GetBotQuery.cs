using MediatR;

namespace ProfitFox.Application.Bots.Queries.GetBot;

public sealed record GetBotQuery(Guid Id) : IRequest<GetBotResult?>;