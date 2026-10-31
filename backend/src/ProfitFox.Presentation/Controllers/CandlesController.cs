using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProfitFox.Application.MarketData.Queries.GetCandles;
using ProfitFox.Presentation.Contracts.MarketData;

namespace ProfitFox.Presentation.Controllers;

[ApiController]
[Route("api/market-data/candles")]
public sealed class CandlesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] CandlesQueryParameters parameters, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetCandlesQuery(parameters.Category, parameters.Symbol, parameters.Interval, parameters.From, parameters.To, parameters.Limit),
            cancellationToken);
        if (result.Candles is null)
        {
            return ValidationProblem(new ValidationProblemDetails(result.Errors.ToDictionary(error => error.Key, error => new[] { error.Value })));
        }

        return Ok(result.Candles.Select(CandleResponse.Of));
    }
}
