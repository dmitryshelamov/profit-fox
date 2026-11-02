using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.Queries.SearchInstruments;
using ProfitFox.Presentation.Contracts.MarketData;

namespace ProfitFox.Presentation.Controllers;

[ApiController]
[Route("api/market-data/instruments")]
public sealed class InstrumentsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<InstrumentResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Search([FromQuery] InstrumentsQueryParameters parameters, CancellationToken cancellationToken)
    {
        SearchInstrumentsResult result;

        try
        {
            result = await sender.Send(new SearchInstrumentsQuery(parameters.Category, parameters.Query, parameters.Limit), cancellationToken);
        }
        catch (ExchangeUnavailableException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (result.Instruments is null)
        {
            return ValidationProblem(new ValidationProblemDetails(result.Errors.ToDictionary(error => error.Key, error => new[] { error.Value })));
        }

        return Ok(result.Instruments.Select(InstrumentResponse.Of));
    }
}