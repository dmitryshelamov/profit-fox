using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Filters.DeleteFilter;
using ProfitFox.Application.Commands.Filters.SaveFilter;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Filters;
using ProfitFox.Domain.Entities.Filters;
using ProfitFox.Domain.Services.Lists;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Filters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/filters</c> (change add-ideas-and-market, spec saved-filters): the saved filters, the fields a condition may
///     have, and the count of the rows a set of conditions matches. An error is told at its place: the field of the request
///     or the number of the condition.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FiltersController(ISender sender) : ControllerBase
{
    [HttpGet(Name = "ListFilters")]
    [ProducesResponseType<FilterResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FilterResponse[]>> List(CancellationToken cancellation)
    {
        return (await sender.Send(new GetFiltersQuery(), cancellation)).Select(Of).ToArray();
    }

    [HttpGet("fields", Name = "ListFilterFields")]
    [ProducesResponseType<FilterFieldResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<FilterFieldResponse[]> Fields()
    {
        return ListFields.Filter
            .Select(field => new FilterFieldResponse(
                field.Name,
                field.Title,
                field.Type.ToString().ToLowerInvariant(),
                field.Place.ToString().ToLowerInvariant(),
                field.Unit,
                field.Values))
            .ToArray();
    }

    [HttpPost(Name = "CreateFilter")]
    [ProducesResponseType<FilterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FilterResponse>> Create(SaveFilterRequest request, CancellationToken cancellation)
    {
        return await Answer<FilterResponse>(async () =>
        {
            SavedFilter filter = await sender.Send(new SaveFilterCommand(null, request.Name, request.Place, Conditions(request.Conditions)), cancellation);
            return CreatedAtRoute("ListFilters", null, Of(filter));
        });
    }

    [HttpPut("{id:guid}", Name = "ReplaceFilter")]
    [ProducesResponseType<FilterResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FilterResponse>> Replace(Guid id, SaveFilterRequest request, CancellationToken cancellation)
    {
        return await Answer<FilterResponse>(async () => Of(await sender.Send(new SaveFilterCommand(id, request.Name, request.Place, Conditions(request.Conditions)), cancellation)));
    }

    [HttpDelete("{id:guid}", Name = "DeleteFilter")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellation)
    {
        return await sender.Send(new DeleteFilterCommand(id), cancellation) ? NoContent() : Problem($"There is no filter {id}.", statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    ///     How many pairs and open ideas the conditions match, of all; a condition with an error answers 422 at its number.
    /// </summary>
    [HttpPost("count", Name = "CountFilter")]
    [ProducesResponseType<FilterCountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FilterCountResponse>> Count(FilterCountRequest request, CancellationToken cancellation)
    {
        return await Answer<FilterCountResponse>(async () =>
        {
            FilterCount count = await sender.Send(new CountFilterQuery(Conditions(request.Conditions)), cancellation);
            return new FilterCountResponse(count.Pairs, count.AllPairs, count.Ideas, count.AllIdeas);
        });
    }

    private static FilterResponse Of(SavedFilter filter)
    {
        return new FilterResponse(filter.Id, filter.Name, FilterRules.Name(filter.Place), [.. filter.Conditions.Select(condition => new FilterConditionDto(condition.Field, condition.Operator, condition.Value, condition.To))]);
    }

    private static IReadOnlyList<FilterCondition>? Conditions(IReadOnlyList<FilterConditionDto>? conditions)
    {
        return conditions?.Select(condition => new FilterCondition(condition.Field ?? "", condition.Operator ?? "", condition.Value, condition.To)).ToArray();
    }

    private async Task<ActionResult<T>> Answer<T>(Func<Task<ActionResult<T>>> act)
    {
        try
        {
            return await act();
        }
        catch (IdeasRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(RequestErrors.Of)]));
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }
}
