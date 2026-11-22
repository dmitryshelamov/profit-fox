using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.Entities.Filters;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Application.Commands.Filters.SaveFilter;

/// <summary>
///     Every problem of the filter at once, each in its place; a taken name is a problem of the name. A filter asked again by
///     an id that is not there is not found.
/// </summary>
internal sealed class SaveFilterCommandHandler(ISavedFilterRepository filters) : IRequestHandler<SaveFilterCommand, SavedFilter>
{
    /// <exception cref="IdeasRequestException">The filter has an error.</exception>
    /// <exception cref="KeyNotFoundException">There is no filter of the id.</exception>
    public async Task<SavedFilter> Handle(SaveFilterCommand request, CancellationToken cancellationToken)
    {
        List<FilterProblem> problems = FilterRules.Check(request.Name, request.Place, request.Conditions);
        if (problems.Count > 0)
        {
            throw FilterResolver.Refused(problems);
        }

        if (request.Id is { } id && await filters.FindAsync(id, cancellationToken) is null)
        {
            throw new KeyNotFoundException($"There is no filter {id}.");
        }

        var filter = new SavedFilter(
            request.Id ?? Guid.NewGuid(),
            request.Name!.Trim(),
            FilterRules.Place(request.Place)!.Value,
            [.. request.Conditions!.Select(condition => condition with { Value = condition.Value?.Trim(), To = condition.To?.Trim() })]);
        if (!await filters.SaveAsync(filter, cancellationToken))
        {
            throw FilterResolver.Refused([new FilterProblem("TakenName", $"фильтр «{filter.Name}» уже есть") { Field = "name" }]);
        }

        return filter;
    }
}
