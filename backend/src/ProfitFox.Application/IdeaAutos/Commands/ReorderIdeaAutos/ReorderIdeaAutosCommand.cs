using MediatR;

namespace ProfitFox.Application.IdeaAutos.Commands.ReorderIdeaAutos;

/// <summary>Puts the autos in the order of the ids: every auto that is not deleted, once.</summary>
public sealed record ReorderIdeaAutosCommand(IReadOnlyList<Guid> Ids) : IRequest;