using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.Commands.CreateCandleImport;
using ProfitFox.Application.MarketData.Queries.GetCandleImport;
using ProfitFox.Presentation.Contracts.MarketData;

namespace ProfitFox.Presentation.Controllers;

[ApiController]
[Route("api/market-data/candle-imports")]
public sealed class CandleImportsController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateCandleImportBody body, CancellationToken cancellationToken)
    {
        Guid id;
        try
        {
            id = await sender.Send(
                new CreateCandleImportCommand(body.Category, body.Symbol, body.Interval, body.From, body.To), cancellationToken);
        }
        catch (ValidationFailedException exception)
        {
            return ValidationProblem(new ValidationProblemDetails(exception.Errors.ToDictionary(error => error.Key, error => new[] { error.Value })));
        }
        catch (ExchangeException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // The background service may already have picked the job up, so its status is not necessarily Pending here.
        var job = await sender.Send(new GetCandleImportQuery(id), cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, CandleImportJobResponse.Of(job!));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var job = await sender.Send(new GetCandleImportQuery(id), cancellationToken);
        return job is null ? NotFound() : Ok(CandleImportJobResponse.Of(job));
    }
}
