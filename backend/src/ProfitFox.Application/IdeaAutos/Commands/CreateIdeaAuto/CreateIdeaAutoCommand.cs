using MediatR;

namespace ProfitFox.Application.IdeaAutos.Commands.CreateIdeaAuto;

/// <summary>Adds an auto at the end of the list, switched off; returns its id.</summary>
public sealed record CreateIdeaAutoCommand(IdeaAutoInput Input) : IRequest<Guid>;