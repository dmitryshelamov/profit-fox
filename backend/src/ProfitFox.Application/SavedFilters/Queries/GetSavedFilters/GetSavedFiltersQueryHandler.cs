using MediatR;

using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.SavedFilters.Queries.GetSavedFilters;

public sealed class GetSavedFiltersQueryHandler(ISavedFilterRepository filters) : IRequestHandler<GetSavedFiltersQuery, IReadOnlyList<SavedFilter>>
{
    public Task<IReadOnlyList<SavedFilter>> Handle(GetSavedFiltersQuery request, CancellationToken cancellationToken)
    {
        return filters.GetAllAsync(cancellationToken);
    }
}