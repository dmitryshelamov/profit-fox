using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.SavedFilters;

namespace ProfitFox.Application.SavedFilters.Commands.UpdateSavedFilter;

public sealed class UpdateSavedFilterCommandHandler(ISavedFilterRepository filters) : IRequestHandler<UpdateSavedFilterCommand>
{
    /// <exception cref="ValidationFailedException">The filter is invalid.</exception>
    /// <exception cref="NotFoundException">There is no such filter.</exception>
    public async Task Handle(UpdateSavedFilterCommand request, CancellationToken cancellationToken)
    {
        (string name, SavedFilterPlace place, IReadOnlyList<FilterCondition> conditions) = SavedFilterRules.Parse(request.Filter);

        if (!await filters.UpdateAsync(new SavedFilter(request.Id, name, place, conditions, default), cancellationToken))
        {
            throw new NotFoundException($"Saved filter {request.Id} not found.");
        }
    }
}