using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.IdeaAutos.Commands.CreateIdeaAuto;
using ProfitFox.Application.IdeaAutos.Commands.DeleteIdeaAuto;
using ProfitFox.Application.IdeaAutos.Commands.ReorderIdeaAutos;
using ProfitFox.Application.IdeaAutos.Commands.SaveIdeaAuto;
using ProfitFox.Application.IdeaAutos.Commands.SetIdeaAutoMode;
using ProfitFox.Application.IdeaAutos.Queries.GetIdeaAuto;
using ProfitFox.Application.IdeaAutos.Queries.GetIdeaAutoRefusals;
using ProfitFox.Application.IdeaAutos.Queries.GetIdeaAutos;
using ProfitFox.Presentation.Contracts.IdeaAutos;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The autos that take ideas by themselves, on a paper or with the money of an account, and the refusals they write.</summary>
[ApiController]
[Route("api/idea-autos")]
public sealed class IdeaAutosController(ISender sender) : ControllerBase
{
    /// <summary>The autos in the order of the list, the first the most important, each with its totals.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<IdeaAutoItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        return Ok((await sender.Send(new GetIdeaAutosQuery(), cancellationToken)).Select(IdeaAutoItemResponse.Of));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<IdeaAutoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        IdeaAutoSummary? auto = await sender.Send(new GetIdeaAutoQuery(id), cancellationToken);

        return auto is null ? NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Detail = $"There is no auto {id}." }) : Ok(IdeaAutoResponse.Of(auto));
    }

    /// <summary>Adds an auto at the end of the list, switched off; a wrong field is a 400 on its name.</summary>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(IdeaAutoBody body, CancellationToken cancellationToken)
    {
        try
        {
            Guid id = await sender.Send(new CreateIdeaAutoCommand(Input(body)), cancellationToken);

            return Created($"/api/idea-autos/{id}", new CreatedResponse(id));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>Writes the name, the account, the amount and the conditions of an auto. The new conditions act on the ideas born after.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Save(Guid id, IdeaAutoBody body, CancellationToken cancellationToken)
    {
        return await Run(new SaveIdeaAutoCommand(id, Input(body)), cancellationToken);
    }

    /// <summary>Changes the mode at once, with no question; the new mode acts on the ideas born after.</summary>
    [HttpPut("{id:guid}/mode")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Mode(Guid id, IdeaAutoModeBody body, CancellationToken cancellationToken)
    {
        return await Run(new SetIdeaAutoModeCommand(id, body.Mode), cancellationToken);
    }

    /// <summary>Puts the autos in the order of the ids, which hold every auto of the list once.</summary>
    [HttpPut("order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Order(IdeaAutoOrderBody body, CancellationToken cancellationToken)
    {
        return await Run(new ReorderIdeaAutosCommand(body.Ids), cancellationToken);
    }

    /// <summary>Takes an auto off the list; its trades stay, the open ones go to their outcome.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await Run(new DeleteIdeaAutoCommand(id), cancellationToken);
    }

    /// <summary>The refusals from the newest: <c>period</c> <c>today</c> (since 00:00 UTC) or <c>7d</c>, one auto or all, a page of them.</summary>
    [HttpGet("refusals")]
    [ProducesResponseType<IdeaAutoRefusalsPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Refusals([FromQuery] string? period, [FromQuery] Guid? autoId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        GetIdeaAutoRefusalsResult result = await sender.Send(new GetIdeaAutoRefusalsQuery(period, autoId, page, pageSize), cancellationToken);

        return result.Refusals is null
            ? ValidationProblem(new ValidationProblemDetails(result.Errors.ToDictionary(error => error.Key, error => new[] { error.Value })))
            : Ok(new IdeaAutoRefusalsPageResponse([.. result.Refusals.Items.Select(IdeaAutoRefusalResponse.Of)], result.Refusals.Number, result.Refusals.Size, result.Refusals.Total));
    }

    private async Task<IActionResult> Run(IRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(request, cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    private static IdeaAutoInput Input(IdeaAutoBody body)
    {
        return new IdeaAutoInput(body.Name, body.AccountId, body.Amount, body.Setups, body.Types, body.Intervals, body.Side, body.Category, body.Badge, body.MinRiskReward, body.MinCases, body.Budget, body.DayDrop, body.BtcDrop, body.HighFunding);
    }
}