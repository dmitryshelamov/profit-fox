using MediatR;

namespace ProfitFox.Application.IdeaTrades.Commands.CreateIdeaTrade;

/// <summary>Enters by an idea: saves a trade that waits for its entry with the entry order; a Worker sends it. Without an account or a risk the settings give them.</summary>
public sealed record CreateIdeaTradeCommand(Guid IdeaId, Guid? AccountId, decimal? Risk) : IRequest<Guid>;