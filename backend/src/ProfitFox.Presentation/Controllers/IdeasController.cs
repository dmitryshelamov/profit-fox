using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.Ideas.Queries.GetIdea;
using ProfitFox.Application.Ideas.Queries.GetIdeaBuckets;
using ProfitFox.Application.Ideas.Queries.GetIdeaCalibration;
using ProfitFox.Application.Ideas.Queries.GetIdeas;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.IdeaTrades.Queries.GetIdeaEntry;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.IdeaTrades;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>The trade ideas the scanner keeps and the statistics of their buckets; reads storage only. Ideas only advise: nothing trades.</summary>
[ApiController]
[Route("api/ideas")]
public sealed class IdeasController(ISender sender) : ControllerBase
{
    /// <summary>One page of the open ideas and the ones closed in the last 24 hours, by a column of the list, the expectancy highest first by default.</summary>
    [HttpGet]
    [ProducesResponseType<IdeasPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] IdeasQueryParameters parameters, CancellationToken cancellationToken)
    {
        GetIdeasResult result = await sender.Send(
            new GetIdeasQuery(
                parameters.Setup,
                parameters.Interval,
                parameters.Type,
                parameters.Side,
                parameters.Category,
                parameters.NonNegativeExpectancy,
                parameters.SavedFilter,
                parameters.Sort,
                parameters.Direction,
                parameters.Page,
                parameters.PageSize),
            cancellationToken);

        return result.Page is null ? Invalid(result.Errors) : Ok(IdeasPageResponse.Of(result));
    }

    [HttpGet("buckets")]
    [ProducesResponseType<IReadOnlyList<IdeaBucketResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Buckets(CancellationToken cancellationToken)
    {
        return Ok((await sender.Send(new GetIdeaBucketsQuery(), cancellationToken)).Select(IdeaBucketResponse.Of).ToList());
    }

    /// <summary>The calibration of the buckets on outcomes they had not seen, as the scanner last computed it.</summary>
    [HttpGet("calibration")]
    [ProducesResponseType<IdeaCalibrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Calibration([FromQuery] IdeaCalibrationParameters parameters, CancellationToken cancellationToken)
    {
        GetIdeaCalibrationResult result = await sender.Send(new GetIdeaCalibrationQuery(parameters.Days, parameters.Category), cancellationToken);

        return result.Errors.Count > 0 ? Invalid(result.Errors) : Ok(IdeaCalibrationResponse.Of(result));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<IdeaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        IdeaView? idea = await sender.Send(new GetIdeaQuery(id), cancellationToken);

        return idea is null ? NotFound() : Ok(IdeaResponse.Of(idea));
    }

    /// <summary>
    ///     The entry of an idea as it would be made now, for an account and an amount (the settings give them when left out): the quantity, the amount,
    ///     the loss at the stop, the profit at the target and the reward to the risk after the fees, and the reasons it cannot be made. Places nothing. Reads the price of the pair from Bybit.
    /// </summary>
    [HttpGet("{id:guid}/entry")]
    [ProducesResponseType<IdeaEntryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Entry(Guid id, [FromQuery] Guid? accountId, [FromQuery] decimal? amount, CancellationToken cancellationToken)
    {
        try
        {
            EntryCalculation? entry = await sender.Send(new GetIdeaEntryQuery(id, accountId, amount), cancellationToken);

            return entry is null ? NotFound() : Ok(IdeaEntryResponse.Of(entry));
        }
        catch (Exception exception) when (StrategyProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    private IActionResult Invalid(IReadOnlyDictionary<string, string> errors)
    {
        return ValidationProblem(new ValidationProblemDetails(errors.ToDictionary(error => error.Key, error => new[] { error.Value })));
    }
}