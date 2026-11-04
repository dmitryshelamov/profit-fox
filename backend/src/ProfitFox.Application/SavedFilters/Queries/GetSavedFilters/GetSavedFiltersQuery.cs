using MediatR;

using ProfitFox.Domain.Entities.SavedFilters;

namespace ProfitFox.Application.SavedFilters.Queries.GetSavedFilters;

/// <summary>Every saved filter, oldest first.</summary>
public sealed record GetSavedFiltersQuery : IRequest<IReadOnlyList<SavedFilter>>;