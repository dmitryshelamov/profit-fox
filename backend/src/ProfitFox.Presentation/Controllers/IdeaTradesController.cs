using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.IdeaTrades.Commands.CancelIdeaTrade;
using ProfitFox.Application.IdeaTrades.Commands.CloseIdeaTrade;
using ProfitFox.Application.IdeaTrades.Commands.CreateIdeaTrade;
using ProfitFox.Application.IdeaTrades.Queries.GetIdeaJournal;
using ProfitFox.Presentation.Contracts.IdeaTrades;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

/// <summary>Trades by ideas: the owner enters by an idea, a trading Worker places the orders and follows the trade.</summary>
[ApiController]
[Route("api/idea-trades")]
public sealed class IdeaTradesController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     The journal: the open trades with what they are worth at the last price, a page of the closed ones, the newest closed first, and the totals of
    ///     all the closed ones of the filter.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IdeaJournalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Journal([FromQuery] IdeaJournalQueryParameters parameters, CancellationToken cancellationToken)
    {
        GetIdeaJournalResult result = await sender.Send(
            new GetIdeaJournalQuery(parameters.Setup, parameters.Symbol, parameters.Side, parameters.Badge, parameters.Days, parameters.Page, parameters.PageSize, parameters.Origin, parameters.AutoId),
            cancellationToken);

        return result.Journal is null
            ? ValidationProblem(new ValidationProblemDetails(result.Errors.ToDictionary(error => error.Key, error => new[] { error.Value })))
            : Ok(IdeaJournalResponse.Of(result.Journal));
    }

    /// <summary>
    ///     Enters by an idea: the entry is calculated again and saved with the trade, which waits for its entry; a Worker sends it. A reason the
    ///     entry cannot be made is a 409 with its codes in <c>reasons</c> and saves nothing.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(IdeaTradeBody body, CancellationToken cancellationToken)
    {
        try
        {
            Guid id = await sender.Send(new CreateIdeaTradeCommand(body.IdeaId, body.AccountId, body.Amount), cancellationToken);

            return Created($"/api/idea-trades/{id}", new CreatedResponse(id));
        }
        catch (Exception exception) when (IdeaTradeProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>Asks a Worker to take the entry off a trade that waits for it.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new CancelIdeaTradeCommand(id), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (IdeaTradeProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>Asks a Worker to take the stop and the target off an open trade and to close it by the market.</summary>
    [HttpPost("{id:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new CloseIdeaTradeCommand(id), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (IdeaTradeProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}