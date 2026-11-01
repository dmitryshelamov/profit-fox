using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Strategies.Commands.CreateStrategy;
using ProfitFox.Application.Strategies.Commands.DeleteStrategy;
using ProfitFox.Application.Strategies.Commands.UpdateStrategy;
using ProfitFox.Application.Strategies.Queries.GetStrategies;
using ProfitFox.Application.Strategies.Queries.GetStrategy;
using ProfitFox.Application.Strategies.Queries.GetStrategyVersion;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

[ApiController]
[Route("api/strategies")]
public sealed class StrategiesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<Strategy> strategies = await sender.Send(new GetStrategiesQuery(), cancellationToken);

        return Ok(strategies.Select(StrategySummaryResponse.Of));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        GetStrategyResult? result = await sender.Send(new GetStrategyQuery(id), cancellationToken);

        return result is null ? NotFound() : Ok(StrategyResponse.Of(result.Strategy, result.Version));
    }

    [HttpGet("{id:guid}/versions/{number:int}")]
    public async Task<IActionResult> GetVersion(Guid id, int number, CancellationToken cancellationToken)
    {
        StrategyVersion? version = await sender.Send(new GetStrategyVersionQuery(id, number), cancellationToken);

        return version is null ? NotFound() : Ok(StrategyVersionResponse.Of(version));
    }

    [HttpPost]
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

    [HttpDelete("{id:guid}")]
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
}