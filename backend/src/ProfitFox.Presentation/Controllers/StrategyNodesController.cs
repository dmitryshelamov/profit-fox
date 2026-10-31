using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.Application.Strategies.Commands.CloneNode;
using ProfitFox.Application.Strategies.Commands.CreateNode;
using ProfitFox.Application.Strategies.Commands.DeleteNode;
using ProfitFox.Application.Strategies.Commands.UpdateNode;
using ProfitFox.Application.Strategies.Queries.GetNode;
using ProfitFox.Application.Strategies.Queries.GetNodeCatalog;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The node library: simple nodes, built-in composite nodes and custom composite nodes; ids are kind codes, built-in codes or custom node ids.</summary>
[ApiController]
[Route("api/strategy-nodes")]
public sealed class StrategyNodesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        IReadOnlyList<NodeCatalogEntry> catalog = await sender.Send(new GetNodeCatalogQuery(), cancellationToken);

        return Ok(catalog.Select(NodeResponse.Of));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        NodeResult? node = await sender.Send(new GetNodeQuery(id), cancellationToken);

        return node is null ? NotFound() : Ok(NodeResponse.Of(node));
    }

    [HttpPost]
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
}