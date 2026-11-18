using Microsoft.AspNetCore.Mvc;
using ProfitFox.Bench.Presentation.Hypotheses;

namespace ProfitFox.Bench.Presentation.Controllers;

/// <summary>
///     <c>GET /api/hypotheses</c> (spec signal-preview «Список гипотез»): the hypotheses the page can take a signal from.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HypothesesController(BenchFolder bench) : ControllerBase
{
    [HttpGet(Name = "ListHypotheses")]
    [ProducesResponseType<HypothesisResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<HypothesisResponse[]> Get()
    {
        return HypothesisList.Read(bench).ToArray();
    }
}
