using Microsoft.AspNetCore.Mvc;

using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Nodes;
using ProfitFox.Presentation.Contracts.Nodes.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>GET /api/nodes</c> (spec signal-constructor «Каталог узлов»): the kinds a signal may have, which the constructor builds
///     its palette from; <c>GET /api/nodes/strategy</c> (spec strategy-constructor «Каталог узлов стратегии»): the kinds of a
///     strategy, of a kind when it is asked.
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

    /// <summary>
    ///     The kinds of a strategy; with <c>kind</c>, <c>bot</c> or <c>idea</c>, only those a strategy of that kind may have.
    /// </summary>
    [HttpGet("strategy", Name = "ListStrategyNodeKinds")]
    [ProducesResponseType<NodeKindResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public ActionResult<NodeKindResponse[]> GetStrategy([FromQuery] string? kind = null)
    {
        if (kind is null)
        {
            return NodeCatalogResponse.BuildStrategy().ToArray();
        }

        return StrategyKinds.TryParse(kind, out StrategyKind strategy)
            ? NodeCatalogResponse.BuildStrategy(strategy).ToArray()
            : BadRequest(new ErrorResponse([RequestErrors.Of(StrategyKinds.Invalid(kind))]));
    }
}
