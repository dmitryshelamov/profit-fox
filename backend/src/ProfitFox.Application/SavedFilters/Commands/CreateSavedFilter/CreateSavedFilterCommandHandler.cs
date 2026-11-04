using MediatR;

using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.SavedFilters;

namespace ProfitFox.Application.SavedFilters.Commands.CreateSavedFilter;

public sealed class CreateSavedFilterCommandHandler(ISavedFilterRepository filters, TimeProvider time) : IRequestHandler<CreateSavedFilterCommand, Guid>
{
    /// <exception cref="Exceptions.ValidationFailedException">The filter is invalid.</exception>
    public async Task<Guid> Handle(CreateSavedFilterCommand request, CancellationToken cancellationToken)
    {
        (string name, SavedFilterPlace place, IReadOnlyList<FilterCondition> conditions) = SavedFilterRules.Parse(request.Filter);
        var filter = new SavedFilter(Guid.NewGuid(), name, place, conditions, time.GetUtcNow());
        await filters.AddAsync(filter, cancellationToken);

        return filter.Id;
    }
}