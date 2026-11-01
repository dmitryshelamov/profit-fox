using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.Application.Strategies;
using ProfitFox.Application.Strategies.Commands.CloneNode;
using ProfitFox.Application.Strategies.Commands.CreateNode;
using ProfitFox.Application.Strategies.Commands.DeleteNode;
using ProfitFox.Application.Strategies.Commands.UpdateNode;
using ProfitFox.Application.Strategies.Queries.GetNode;
using ProfitFox.Application.Strategies.Queries.GetNodeCatalog;
using ProfitFox.Application.Strategies.Queries.ValidateNode;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The node library: simple nodes, built-in composite nodes and custom composite nodes; ids are kind codes, built-in codes or custom node ids.</summary>
[ApiController]
[Route("api/strategy-nodes")]
public sealed class StrategyNodesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NodeResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        IReadOnlyList<NodeCatalogEntry> catalog = await sender.Send(new GetNodeCatalogQuery(), cancellationToken);

        return Ok(catalog.Select(NodeResponse.Of));
    }

    [HttpGet("{id}")]
    [ProducesResponseType<NodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        NodeResult? node = await sender.Send(new GetNodeQuery(id), cancellationToken);

        return node is null ? NotFound() : Ok(NodeResponse.Of(node));
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(DefinitionBody body, CancellationToken cancellationToken)
    {
        try
        {
            Guid id = await sender.Send(new CreateNodeCommand(body.Name, body.Definition!.Value), cancellationToken);

            return CreatedAtAction(nameof(Get), new { id }, new CreatedResponse(id));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, DefinitionBody body, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new UpdateNodeCommand(id, body.Name, body.Definition!.Value), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new DeleteNodeCommand(id), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpPost("{id}/clone")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Clone(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CloneNodeBody? body, CancellationToken cancellationToken)
    {
        try
        {
            Guid clone = await sender.Send(new CloneNodeCommand(id, body?.Name), cancellationToken);

            return CreatedAtAction(nameof(Get), new { id = clone }, new CreatedResponse(clone));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>What creating this composite node would reject; nothing is saved.</summary>
    [HttpPost("validate")]
    [ProducesResponseType<ValidationReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ValidateNew(DefinitionBody body, CancellationToken cancellationToken)
    {
        return Validate(null, body, cancellationToken);
    }

    /// <summary>What saving this change of the composite node would reject; nothing is saved.</summary>
    [HttpPost("{id}/validate")]
    [ProducesResponseType<ValidationReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ValidateChange(string id, DefinitionBody body, CancellationToken cancellationToken)
    {
        return Validate(id, body, cancellationToken);
    }

    private async Task<IActionResult> Validate(string? id, DefinitionBody body, CancellationToken cancellationToken)
    {
        try
        {
            ValidationReport report = await sender.Send(new ValidateNodeQuery(id, body.Name, body.Definition!.Value), cancellationToken);

            return Ok(ValidationReportResponse.Of(report));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}