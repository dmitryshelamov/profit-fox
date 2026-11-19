using Microsoft.AspNetCore.Mvc;
using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Domain.Hypotheses;
using ProfitFox.Bench.Presentation.Hypotheses;

namespace ProfitFox.Bench.Presentation.Controllers;

/// <summary>
///     <c>GET /api/hypotheses</c> (spec signal-preview «Список гипотез»): the hypotheses the page can take a signal from.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HypothesesController(HypothesisCatalog hypotheses) : ControllerBase
{
    [HttpGet(Name = "ListHypotheses")]
    [ProducesResponseType<HypothesisResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<HypothesisResponse[]> Get()
    {
        return hypotheses.List()
            .Select(item => new HypothesisResponse(item.Id, item.Name, item.Timeframe is { } timeframe ? Timeframes.Name(timeframe) : null, item.Signal, item.Error))
            .ToArray();
    }
}
