using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Presentation.Contracts.Instruments;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>GET /api/instruments</c> (spec signal-preview «Список пар»): the pairs of the bench run, ordered by symbol.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InstrumentsController(IInstrumentCatalog catalog) : ControllerBase
{
    [HttpGet(Name = "ListInstruments")]
    [ProducesResponseType<InstrumentResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<InstrumentResponse[]>> Get(CancellationToken cancellation)
    {
        try
        {
            IReadOnlyList<Instrument> instruments = await catalog.LinearUsdtPerpetuals(cancellation);
            return instruments.Select(instrument => new InstrumentResponse(instrument.Id, instrument.Symbol)).ToArray();
        }
        catch (HistoryUnavailableException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
