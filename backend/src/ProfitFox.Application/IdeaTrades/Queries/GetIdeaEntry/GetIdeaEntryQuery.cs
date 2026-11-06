using MediatR;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaEntry;

/// <summary>The entry of an idea as it would be made now; without an account or a risk the settings give them. None when there is no such idea.</summary>
public sealed record GetIdeaEntryQuery(Guid IdeaId, Guid? AccountId, decimal? Risk) : IRequest<EntryCalculation?>;