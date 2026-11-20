using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ProfitFox.Bench.Application.Signals;
using ProfitFox.Bench.Domain.Signals;
using ProfitFox.Bench.Presentation.Errors;
using ProfitFox.Bench.Presentation.Signals;
using ProfitFox.Engine.Strategy.Graph;
using ProfitFox.Engine.Strategy.Graph.Reading;

namespace ProfitFox.Bench.Presentation.Controllers;

/// <summary>
///     <c>/api/signals</c> (spec signal-constructor): the saved signals of the bench and the check of a graph without
///     history. A graph is saved only when it reads as a signal; a refused request leaves the signals as they were.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SignalsController(SignalLibrary library) : ControllerBase
{
    private static readonly Dictionary<string, NodePosition> NoLayout = [];

    [HttpGet(Name = "ListSignals")]
    [ProducesResponseType<SignalListItem[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignalListItem[]>> List(CancellationToken cancellation)
    {
        return (await library.List(cancellation)).Select(entry => new SignalListItem(entry.Name, entry.Description, entry.Version, entry.Error)).ToArray();
    }

    [HttpGet("{name}", Name = "GetSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SignalResponse>> Get(string name, CancellationToken cancellation)
    {
        return await Answer(async () => SignalResponses.Of(await library.Read(name, cancellation)));
    }

    [HttpPost(Name = "CreateSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SignalResponse>> Create(CreateSignalRequest request, CancellationToken cancellation)
    {
        if (request.Graph is not { } graph)
        {
            return MissingGraph();
        }

        ActionResult<SignalResponse> answer = await Answer(async () =>
            SignalResponses.Of(await library.Create(request.Name ?? "", request.Description, graph, request.Layout ?? NoLayout, cancellation)));
        return answer.Value is { } created ? CreatedAtRoute("GetSignal", new { name = created.Name }, created) : answer;
    }

    [HttpPut("{name}", Name = "ReplaceSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SignalResponse>> Replace(string name, ReplaceSignalRequest request, CancellationToken cancellation)
    {
        if (request.Graph is not { } graph)
        {
            return MissingGraph();
        }

        return await Answer(async () => SignalResponses.Of(await library.Replace(name, request.Description, graph, request.Layout ?? NoLayout, cancellation)));
    }

    [HttpPost("{name}/rename", Name = "RenameSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SignalResponse>> Rename(string name, RenameSignalRequest request, CancellationToken cancellation)
    {
        return await Answer(async () => SignalResponses.Of(await library.Rename(name, request.To ?? "", cancellation)));
    }

    [HttpDelete("{name}", Name = "DeleteSignal")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(string name, CancellationToken cancellation)
    {
        ActionResult<bool> answer = await Answer(async () =>
        {
            await library.Delete(name, cancellation);
            return true;
        });
        return answer.Result ?? NoContent();
    }

    [HttpPost("validate", Name = "ValidateSignalGraph")]
    [ProducesResponseType<ValidationResponse>(StatusCodes.Status200OK)]
    public ActionResult<ValidationResponse> Validate(ValidateGraphRequest request)
    {
        if (request.Graph is not { } graph)
        {
            return new ValidationResponse([new RequestError("MissingField", "the graph is required") { Field = "graph" }]);
        }

        ReadResult<Signal> read = StrategyGraph.ReadSignal(graph);
        return new ValidationResponse([.. read.Errors.Select(RequestErrors.Of)]);
    }

    private async Task<ActionResult<T>> Answer<T>(Func<Task<T>> act)
    {
        try
        {
            return await act();
        }
        catch (SignalLibraryException exception)
        {
            return exception.Problem switch
            {
                SignalProblem.Invalid => BadRequest(new ErrorResponse([.. exception.Errors.Select(RequestErrors.Of)])),
                SignalProblem.NotFound => Problem(exception.Message, statusCode: StatusCodes.Status404NotFound),
                SignalProblem.NameTaken => Conflict(new ErrorResponse([new RequestError("NameTaken", exception.Message) { Field = "name" }])),
                SignalProblem.Unreadable => UnprocessableEntity(new ErrorResponse([new RequestError("UnreadableSignal", exception.Message)])),
                _ => throw new InvalidOperationException($"Unknown problem {exception.Problem}.", exception)
            };
        }
    }

    private BadRequestObjectResult MissingGraph()
    {
        return BadRequest(new ErrorResponse([new RequestError("MissingField", "the graph is required") { Field = "graph" }]));
    }
}
