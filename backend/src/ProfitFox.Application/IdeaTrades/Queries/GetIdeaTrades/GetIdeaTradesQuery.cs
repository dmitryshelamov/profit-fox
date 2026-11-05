using MediatR;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaTrades;

/// <summary>Every trade by an idea, the newest first, with the name of its account.</summary>
public sealed record GetIdeaTradesQuery : IRequest<IReadOnlyList<IdeaTradeView>>;