using MediatR;

namespace ProfitFox.Application.IdeaTrades.Commands.CreateIdeaTrade;

/// <summary>Enters by an idea: saves a trade that waits for its entry with the entry order; a Worker sends it. Without an account the settings give it.</summary>
public sealed record CreateIdeaTradeCommand(Guid IdeaId, Guid? AccountId, decimal Amount) : IRequest<Guid>;