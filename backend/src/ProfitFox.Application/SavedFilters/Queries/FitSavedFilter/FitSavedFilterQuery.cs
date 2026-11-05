using MediatR;

namespace ProfitFox.Application.SavedFilters.Queries.FitSavedFilter;

/// <summary>How many pairs of the snapshot and how many ideas of the list meet the conditions of a draft of a filter; saves nothing.</summary>
public sealed record FitSavedFilterQuery(IReadOnlyList<SavedFilterConditionInput>? Conditions) : IRequest<FitSavedFilterResult>;