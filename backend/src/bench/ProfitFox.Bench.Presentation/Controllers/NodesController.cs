using Microsoft.AspNetCore.Mvc;
using ProfitFox.Bench.Presentation.Nodes;

namespace ProfitFox.Bench.Presentation.Controllers;

/// <summary>
///     <c>GET /api/nodes</c> (spec signal-constructor «Каталог узлов»): the kinds a signal may have, which the constructor builds
///     its palette from.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class NodesController : ControllerBase
{
    [HttpGet(Name = "ListNodeKinds")]
    [ProducesResponseType<NodeKindResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<NodeKindResponse[]> Get()
    {
        return NodeCatalogResponse.Build().ToArray();
    }
}
