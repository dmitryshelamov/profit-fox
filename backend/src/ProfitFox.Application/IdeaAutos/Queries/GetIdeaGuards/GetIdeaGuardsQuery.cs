using MediatR;

namespace ProfitFox.Application.IdeaAutos.Queries.GetIdeaGuards;

/// <summary>The state of the guards of the real trades: the limits of the settings against what is open and what the day lost.</summary>
public sealed record GetIdeaGuardsQuery : IRequest<IdeaGuardsState>;