using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.Ideas.Queries.GetIdea;
using ProfitFox.Application.Ideas.Queries.GetIdeaBuckets;
using ProfitFox.Application.Ideas.Queries.GetIdeas;
using ProfitFox.Presentation.Contracts.Ideas;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The trade ideas the scanner keeps and the statistics of their buckets; reads storage only. Ideas only advise: nothing trades.</summary>
[ApiController]
[Route("api/ideas")]
public sealed class IdeasController(ISender sender) : ControllerBase
{
    /// <summary>The open ideas and the ones closed in the last 24 hours, by expectancy, highest first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<IdeaResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] IdeasQueryParameters parameters, CancellationToken cancellationToken)
    {
        GetIdeasResult result = await sender.Send(
            new GetIdeasQuery(parameters.Setup, parameters.Interval, parameters.Type, parameters.Side, parameters.Category, parameters.NonNegativeExpectancy),
            cancellationToken);

        return result.Ideas is null ? Invalid(result.Errors) : Ok(result.Ideas.Select(IdeaResponse.Of).ToList());
    }

    [HttpGet("buckets")]
    [ProducesResponseType<IReadOnlyList<IdeaBucketResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Buckets(CancellationToken cancellationToken)
    {
        return Ok((await sender.Send(new GetIdeaBucketsQuery(), cancellationToken)).Select(IdeaBucketResponse.Of).ToList());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<IdeaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        IdeaView? idea = await sender.Send(new GetIdeaQuery(id), cancellationToken);

        return idea is null ? NotFound() : Ok(IdeaResponse.Of(idea));
    }

    private IActionResult Invalid(IReadOnlyDictionary<string, string> errors)
    {
        return ValidationProblem(new ValidationProblemDetails(errors.ToDictionary(error => error.Key, error => new[] { error.Value })));
    }
}