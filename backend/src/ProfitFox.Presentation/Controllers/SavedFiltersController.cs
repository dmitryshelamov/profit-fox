using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.SavedFilters;
using ProfitFox.Application.SavedFilters.Commands.CreateSavedFilter;
using ProfitFox.Application.SavedFilters.Commands.DeleteSavedFilter;
using ProfitFox.Application.SavedFilters.Commands.UpdateSavedFilter;
using ProfitFox.Application.SavedFilters.Queries.GetSavedFilters;
using ProfitFox.Presentation.Contracts.SavedFilters;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>Named filters of «Рынок» and «Идеи» (decision 8); the browser applies them to the snapshot and the ideas it has.</summary>
[ApiController]
[Route("api/saved-filters")]
public sealed class SavedFiltersController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SavedFilterResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        return Ok((await sender.Send(new GetSavedFiltersQuery(), cancellationToken)).Select(SavedFilterResponse.Of).ToList());
    }

    /// <summary>The catalog of the fields a condition may test, with their units and operators.</summary>
    [HttpGet("fields")]
    [ProducesResponseType<IReadOnlyList<SavedFilterFieldResponse>>(StatusCodes.Status200OK)]
    public IActionResult Fields()
    {
        return Ok(SavedFilterFields.All.Select(SavedFilterFieldResponse.Of).ToList());
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(SavedFilterBody body, CancellationToken cancellationToken)
    {
        try
        {
            Guid id = await sender.Send(new CreateSavedFilterCommand(Input(body)), cancellationToken);

            return Created($"/api/saved-filters/{id}", new CreatedResponse(id));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, SavedFilterBody body, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new UpdateSavedFilterCommand(id, Input(body)), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new DeleteSavedFilterCommand(id), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    private static SavedFilterInput Input(SavedFilterBody body)
    {
        return new SavedFilterInput(
            body.Name,
            body.Place,
            body.Conditions?.Select(condition => new SavedFilterConditionInput(condition.Field, condition.Operator, condition.Value, condition.To)).ToList());
    }
}