using MediatR;

using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades.Commands.ClaimIdeaTrade;

/// <summary>Takes a trade by an idea a Worker must keep and nobody keeps; <c>null</c> when there is none.</summary>
public sealed record ClaimIdeaTradeCommand : IRequest<LeasedIdeaTrade?>;