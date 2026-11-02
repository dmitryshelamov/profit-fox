using MediatR;

using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Application.LiveTrading.Commands.ClaimLiveBot;

/// <summary>Takes a bot a Worker must keep and nobody keeps; <c>null</c> when there is none.</summary>
public sealed record ClaimLiveBotCommand : IRequest<LeasedBot?>;