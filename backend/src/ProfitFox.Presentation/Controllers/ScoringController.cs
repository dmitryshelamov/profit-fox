using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Scoring.SetLabelWeight;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Scoring;
using ProfitFox.Domain.ValueObjects.Scoring;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Scoring;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/scoring</c> (change add-idea-score, spec idea-score; design.md, decision 3): the labels with their weights and
///     the weights of one label. The score of an idea is the sum of the weights of its labels by its side, by the weights now.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ScoringController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     A row a text of the labels of signals, those that are on first, and the built-in label «⚡ всплеск» last.
    /// </summary>
    [HttpGet("labels", Name = "ListScoringLabels")]
    [ProducesResponseType<ScoringLabelResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ScoringLabelResponse[]>> Labels(CancellationToken cancellation)
    {
        return (await sender.Send(new GetScoringLabelsQuery(), cancellation)).Select(Of).ToArray();
    }

    /// <summary>
    ///     Sets the weights of a label: 200 with its row, 404 for a text no label has, 422 for a weight that is not a whole
    ///     number from −3 to +3.
    /// </summary>
    [HttpPut("weights", Name = "SetLabelWeight")]
    [ProducesResponseType<ScoringLabelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScoringLabelResponse>> SetWeight(LabelWeightRequest request, CancellationToken cancellation)
    {
        try
        {
            return Of(await sender.Send(new SetLabelWeightCommand(request.Text, request.LongWeight, request.ShortWeight), cancellation));
        }
        catch (IdeasRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(RequestErrors.Of)]));
        }
        catch (LabelNotFoundException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static ScoringLabelResponse Of(ScoringLabel label)
    {
        return new ScoringLabelResponse(label.Text, label.Enabled, label.Timeframe, label.Signal, label.LongWeight, label.ShortWeight, label.BuiltIn);
    }
}
