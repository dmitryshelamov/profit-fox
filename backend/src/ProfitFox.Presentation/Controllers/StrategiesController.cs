using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Strategies;
using ProfitFox.Application.Strategies.Commands.CreateStrategy;
using ProfitFox.Application.Strategies.Commands.DeleteStrategy;
using ProfitFox.Application.Strategies.Commands.UpdateStrategy;
using ProfitFox.Application.Strategies.Queries.GetStrategies;
using ProfitFox.Application.Strategies.Queries.GetStrategy;
using ProfitFox.Application.Strategies.Queries.GetStrategyVersion;
using ProfitFox.Application.Strategies.Queries.ValidateStrategy;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

[ApiController]
[Route("api/strategies")]
public sealed class StrategiesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StrategySummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<Strategy> strategies = await sender.Send(new GetStrategiesQuery(), cancellationToken);

        return Ok(strategies.Select(StrategySummaryResponse.Of));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<StrategyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        GetStrategyResult? result = await sender.Send(new GetStrategyQuery(id), cancellationToken);

        return result is null ? NotFound() : Ok(StrategyResponse.Of(result.Strategy, result.Version));
    }

    [HttpGet("{id:guid}/versions/{number:int}")]
    [ProducesResponseType<StrategyVersionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(Guid id, int number, CancellationToken cancellationToken)
    {
        StrategyVersion? version = await sender.Send(new GetStrategyVersionQuery(id, number), cancellationToken);

        return version is null ? NotFound() : Ok(StrategyVersionResponse.Of(version));
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(DefinitionBody body, CancellationToken cancellationToken)
    {
        try
        {
            Guid id = await sender.Send(new CreateStrategyCommand(body.Name, body.Definition!.Value), cancellationToken);

            return CreatedAtAction(nameof(Get), new { id }, new CreatedResponse(id));
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, DefinitionBody body, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new UpdateStrategyCommand(id, body.Name, body.Definition!.Value), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>What creating this strategy would reject; nothing is saved.</summary>
    [HttpPost("validate")]
    [ProducesResponseType<ValidationReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ValidateNew(DefinitionBody body, CancellationToken cancellationToken)
    {
        return Validate(null, body, cancellationToken);
    }

    /// <summary>What saving this change of the strategy would reject; nothing is saved.</summary>
    [HttpPost("{id:guid}/validate")]
    [ProducesResponseType<ValidationReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ValidateChange(Guid id, DefinitionBody body, CancellationToken cancellationToken)
    {
        return Validate(id, body, cancellationToken);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new DeleteStrategyCommand(id), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    private async Task<IActionResult> Validate(Guid? id, DefinitionBody body, CancellationToken cancellationToken)
    {
        try
        {
            ValidationReport report = await sender.Send(new ValidateStrategyQuery(id, body.Name, body.Definition!.Value), cancellationToken);

            return Ok(ValidationReportResponse.Of(report));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}