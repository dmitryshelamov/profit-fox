using Microsoft.AspNetCore.Mvc;

using ProfitFox.Presentation.Contracts.Nodes;
using ProfitFox.Presentation.Contracts.Nodes.Converters;

namespace ProfitFox.Presentation.Controllers;

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
