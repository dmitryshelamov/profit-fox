using MediatR;

using ProfitFox.Domain.Entities.Filters;
using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Application.Queries.Filters;

/// <summary>
///     The saved filters by name (spec saved-filters «Сохранённый фильтр»).
/// </summary>
public sealed record GetFiltersQuery : IRequest<IReadOnlyList<SavedFilter>>;

/// <summary>
///     How many pairs and open ideas a set of conditions not saved yet matches, of all (spec saved-filters «Счётчик
///     подходящих строк»).
/// </summary>
public sealed record CountFilterQuery(IReadOnlyList<FilterCondition>? Conditions) : IRequest<FilterCount>;

internal sealed class GetFiltersQueryHandler(Domain.Repositories.ISavedFilterRepository filters, Domain.Repositories.IMarketPairRepository market)
    : IRequestHandler<GetFiltersQuery, IReadOnlyList<SavedFilter>>, IRequestHandler<CountFilterQuery, FilterCount>
{
    public Task<IReadOnlyList<SavedFilter>> Handle(GetFiltersQuery request, CancellationToken cancellationToken)
    {
        return filters.AllAsync(cancellationToken);
    }

    /// <exception cref="Exceptions.IdeasRequestException">A condition has an error.</exception>
    public async Task<FilterCount> Handle(CountFilterQuery request, CancellationToken cancellationToken)
    {
        List<FilterProblem> problems = Domain.Services.Lists.FilterRules.CheckConditions(request.Conditions);
        if (problems.Count > 0)
        {
            throw FilterResolver.Refused(problems);
        }

        return await market.CountAsync(request.Conditions!, cancellationToken);
    }
}
