using MediatR;

namespace ProfitFox.Application.IdeaTrades.Commands.CloseIdeaTrade;

/// <summary>Closes an open trade by the market: a Worker takes its stop and target off and sells what it holds.</summary>
public sealed record CloseIdeaTradeCommand(Guid Id) : IRequest;