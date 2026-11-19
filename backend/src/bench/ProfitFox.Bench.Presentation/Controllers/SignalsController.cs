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
///     <c>/api/signals</c> (spec signal-constructor): the saved signals of <c>bench/signals</c> and the check of a graph
///     without history. A graph is saved only when it reads as a signal; a refused request leaves the files as they were.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SignalsController(SignalLibrary library) : ControllerBase
{
    private static readonly Dictionary<string, NodePosition> NoLayout = [];

    [HttpGet(Name = "ListSignals")]
    [ProducesResponseType<SignalListItem[]>(StatusCodes.Status200OK)]
    public ActionResult<SignalListItem[]> List()
    {
        return library.List().Select(entry => new SignalListItem(entry.Name, entry.Description, entry.Error)).ToArray();
    }

    [HttpGet("{name}", Name = "GetSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public ActionResult<SignalResponse> Get(string name)
    {
        return Answer(() => SignalResponses.Of(library.Read(name)));
    }

    [HttpPost(Name = "CreateSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public ActionResult<SignalResponse> Create(CreateSignalRequest request)
    {
        if (request.Graph is not { } graph)
        {
            return MissingGraph();
        }

        ActionResult<SignalResponse> answer = Answer(() => SignalResponses.Of(library.Create(request.Name ?? "", request.Description, graph, request.Layout ?? NoLayout)));
        return answer.Value is { } created ? CreatedAtRoute("GetSignal", new { name = created.Name }, created) : answer;
    }

    [HttpPut("{name}", Name = "ReplaceSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<SignalResponse> Replace(string name, ReplaceSignalRequest request)
    {
        if (request.Graph is not { } graph)
        {
            return MissingGraph();
        }

        return Answer(() => SignalResponses.Of(library.Replace(name, request.Description, graph, request.Layout ?? NoLayout)));
    }

    [HttpPost("{name}/rename", Name = "RenameSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public ActionResult<SignalResponse> Rename(string name, RenameSignalRequest request)
    {
        return Answer(() => SignalResponses.Of(library.Rename(name, request.To ?? "")));
    }

    [HttpDelete("{name}", Name = "DeleteSignal")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult Delete(string name)
    {
        ActionResult<bool> answer = Answer(() =>
        {
            library.Delete(name);
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

    private ActionResult<T> Answer<T>(Func<T> act)
    {
        try
        {
            return act();
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
