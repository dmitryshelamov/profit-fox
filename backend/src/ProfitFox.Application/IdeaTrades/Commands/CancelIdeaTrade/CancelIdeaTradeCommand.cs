using MediatR;

namespace ProfitFox.Application.IdeaTrades.Commands.CancelIdeaTrade;

/// <summary>Cancels the entry of a trade that waits for it: a Worker takes the entry off.</summary>
public sealed record CancelIdeaTradeCommand(Guid Id) : IRequest;