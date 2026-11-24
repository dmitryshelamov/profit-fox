using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Strategies.CreateStrategy;
using ProfitFox.Application.Commands.Strategies.DeleteStrategy;
using ProfitFox.Application.Commands.Strategies.RenameStrategy;
using ProfitFox.Application.Commands.Strategies.ReplaceStrategy;
using ProfitFox.Application.Commands.Strategies.SetStrategyIdeas;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Strategies;
using ProfitFox.Presentation.Contracts.Strategies.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/strategies</c> (spec strategy-constructor): the saved strategies and the check of a graph as a strategy
///     without history; the routes mirror those of the signals. A graph is saved only when it reads as a strategy with the
///     saved signals it points at and by the rules of its kind; a refused request leaves the strategies as they were.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StrategiesController(ISender sender) : ControllerBase
{
    private static readonly Dictionary<string, NodePosition> NoLayout = [];

    [HttpGet(Name = "ListStrategies")]
    [ProducesResponseType<StrategyListItem[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StrategyListItem[]>> List(CancellationToken cancellation)
    {
        return (await sender.Send(new GetStrategiesQuery(), cancellation))
            .Select(entry => new StrategyListItem(
                entry.Name,
                entry.Description,
                entry.Version,
                entry.Error,
                [.. entry.Signals.Select(signal => new StrategySignalItem(signal.Name, signal.Version, signal.LatestVersion))],
                entry.Exits,
                entry.Kind is { } kind ? StrategyKinds.Name(kind) : null,
                entry.LastRun is { } last ? new StrategyLastRunResponse(last.Id, last.Status, last.Version) : null,
                entry.Ideas is { } ideas ? IdeasOf(ideas) : null))
            .ToArray();
    }

    /// <summary>
    ///     Turns the ideas of a strategy of the kind «категория идей» on or off on its timeframes (change add-ideas-and-market,
    ///     spec strategy-ideas «Включение идей у стратегии»). A strategy for a bot, a version that does not pass the checks and
    ///     an empty set of timeframes are refused with every error.
    /// </summary>
    [HttpPut("{name}/ideas", Name = "SetStrategyIdeas")]
    [ProducesResponseType<StrategyIdeasResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StrategyIdeasResponse>> SetIdeas(string name, StrategyIdeasRequest request, CancellationToken cancellation)
    {
        try
        {
            return await Answer(async () => IdeasOf(await sender.Send(new SetStrategyIdeasCommand(name, request.Enabled, request.Timeframes), cancellation)));
        }
        catch (IdeasRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(RequestErrors.Of)]));
        }
    }

    private static StrategyIdeasResponse IdeasOf(StrategyIdeas ideas)
    {
        return new StrategyIdeasResponse(ideas.Enabled, [.. ideas.Timeframes.Select(Timeframes.Name)]);
    }

    [HttpGet("{name}", Name = "GetStrategy")]
    [ProducesResponseType<StrategyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StrategyResponse>> Get(string name, CancellationToken cancellation)
    {
        return await Answer(async () => StrategyResponses.Of(await sender.Send(new GetStrategyQuery(name), cancellation)));
    }

    /// <summary>
    ///     The version <c>version</c> of the strategy: what the form of a backtest is of.
    /// </summary>
    [HttpGet("{name}/versions/{version:int}", Name = "GetStrategyVersion")]
    [ProducesResponseType<StrategyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StrategyResponse>> GetVersion(string name, int version, CancellationToken cancellation)
    {
        return await Answer(async () => StrategyResponses.Of(await sender.Send(new GetStrategyVersionQuery(name, version), cancellation)));
    }

    [HttpPost(Name = "CreateStrategy")]
    [ProducesResponseType<StrategyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StrategyResponse>> Create(CreateStrategyRequest request, CancellationToken cancellation)
    {
        if (request.Graph is not { } graph)
        {
            return MissingGraph();
        }

        if (!Kind(request.Kind, out StrategyKind? kind))
        {
            return InvalidKind(request.Kind);
        }

        ActionResult<StrategyResponse> answer = await Answer(async () =>
            StrategyResponses.Of(await sender.Send(new CreateStrategyCommand(request.Name ?? "", request.Description, graph, request.Layout ?? NoLayout, kind), cancellation)));
        return answer.Value is { } created ? CreatedAtRoute("GetStrategy", new { name = created.Name }, created) : answer;
    }

    [HttpPut("{name}", Name = "ReplaceStrategy")]
    [ProducesResponseType<StrategyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StrategyResponse>> Replace(string name, ReplaceStrategyRequest request, CancellationToken cancellation)
    {
        if (request.Graph is not { } graph)
        {
            return MissingGraph();
        }

        if (!Kind(request.Kind, out StrategyKind? kind))
        {
            return InvalidKind(request.Kind);
        }

        return await Answer(async () =>
            StrategyResponses.Of(await sender.Send(new ReplaceStrategyCommand(name, request.Description, graph, request.Layout ?? NoLayout, kind), cancellation)));
    }

    [HttpPost("{name}/rename", Name = "RenameStrategy")]
    [ProducesResponseType<StrategyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StrategyResponse>> Rename(string name, RenameStrategyRequest request, CancellationToken cancellation)
    {
        return await Answer(async () => StrategyResponses.Of(await sender.Send(new RenameStrategyCommand(name, request.To ?? ""), cancellation)));
    }

    [HttpDelete("{name}", Name = "DeleteStrategy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(string name, CancellationToken cancellation)
    {
        ActionResult<bool> answer = await Answer(async () =>
        {
            await sender.Send(new DeleteStrategyCommand(name), cancellation);
            return true;
        });
        return answer.Result ?? NoContent();
    }

    [HttpPost("validate", Name = "ValidateStrategyGraph")]
    [ProducesResponseType<StrategyValidationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StrategyValidationResponse>> Validate(ValidateStrategyGraphRequest request, CancellationToken cancellation)
    {
        List<RequestError> errors = [];
        if (request.Graph is null)
        {
            errors.Add(new RequestError("MissingField", "the graph is required") { Field = "graph" });
        }

        if (!Kind(request.Kind, out StrategyKind? kind))
        {
            errors.Add(RequestErrors.Of(StrategyKinds.Invalid(request.Kind)));
        }

        if (request.Graph is not { } graph || errors.Count > 0)
        {
            return new StrategyValidationResponse(errors, []);
        }

        StrategyCheckInternal check = await sender.Send(new CheckStrategyGraphQuery(graph, kind ?? StrategyKind.Bot), cancellation);
        return new StrategyValidationResponse([.. check.Errors.Select(RequestErrors.Of)], [.. check.Signals.Select(StrategyResponses.Of)]);
    }

    private async Task<ActionResult<T>> Answer<T>(Func<Task<T>> act)
    {
        try
        {
            return await act();
        }
        catch (StrategyLibraryException exception)
        {
            return exception.Problem switch
            {
                StrategyProblem.Invalid => BadRequest(new ErrorResponse(Errors(exception))),
                StrategyProblem.NotFound => Problem(exception.Message, statusCode: StatusCodes.Status404NotFound),
                StrategyProblem.NameTaken => Conflict(new ErrorResponse([new RequestError("NameTaken", exception.Message) { Field = "name" }])),
                StrategyProblem.Unreadable => UnprocessableEntity(new ErrorResponse([new RequestError("UnreadableStrategy", exception.Message)])),
                _ => throw new InvalidOperationException($"Unknown problem {exception.Problem}.", exception)
            };
        }
    }

    // A refusal without graph errors is a signal deleted between the check and the write.
    private static RequestError[] Errors(StrategyLibraryException exception)
    {
        return exception.Errors.Count > 0
            ? [.. exception.Errors.Select(RequestErrors.Of)]
            : [new RequestError("UnknownSignal", exception.Message)];
    }

    // The kind of a request: none, or one of bot and idea.
    private static bool Kind(string? name, out StrategyKind? kind)
    {
        kind = null;
        if (name is null)
        {
            return true;
        }

        if (!StrategyKinds.TryParse(name, out StrategyKind parsed))
        {
            return false;
        }

        kind = parsed;
        return true;
    }

    private BadRequestObjectResult InvalidKind(string? name)
    {
        return BadRequest(new ErrorResponse([RequestErrors.Of(StrategyKinds.Invalid(name))]));
    }

    private BadRequestObjectResult MissingGraph()
    {
        return BadRequest(new ErrorResponse([new RequestError("MissingField", "the graph is required") { Field = "graph" }]));
    }
}
