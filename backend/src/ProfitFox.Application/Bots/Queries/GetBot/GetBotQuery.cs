using MediatR;

using ProfitFox.Domain.Entities.Bots;

namespace ProfitFox.Application.Bots.Queries.GetBot;

/// <summary>The bot with this id, or <c>null</c> when there is none.</summary>
public sealed record GetBotQuery(Guid Id) : IRequest<Bot?>;