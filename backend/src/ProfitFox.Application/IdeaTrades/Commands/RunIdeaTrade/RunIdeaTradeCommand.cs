using MediatR;

using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades.Commands.RunIdeaTrade;

/// <summary>Keeps a taken trade until it is done, its lease is lost or the Worker stops.</summary>
public sealed record RunIdeaTradeCommand(LeasedIdeaTrade Trade) : IRequest;