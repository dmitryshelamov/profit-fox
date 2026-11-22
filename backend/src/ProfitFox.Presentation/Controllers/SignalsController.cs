using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Signals.CreateSignal;
using ProfitFox.Application.Commands.Signals.DeleteSignal;
using ProfitFox.Application.Commands.Signals.RenameSignal;
using ProfitFox.Application.Commands.Signals.ReplaceSignal;
using ProfitFox.Application.Commands.Signals.SetSignalLabel;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Errors.Converters;
using ProfitFox.Presentation.Contracts.Ideas;
using ProfitFox.Presentation.Contracts.Signals;
using ProfitFox.Presentation.Contracts.Signals.Converters;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/signals</c> (spec signal-constructor): the saved signals of the bench and the check of a graph without
///     history. A graph is saved only when it reads as a signal; a refused request leaves the signals as they were.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SignalsController(ISender sender) : ControllerBase
{
    private static readonly Dictionary<string, NodePosition> NoLayout = [];

    [HttpGet(Name = "ListSignals")]
    [ProducesResponseType<SignalListItem[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignalListItem[]>> List(CancellationToken cancellation)
    {
        return (await sender.Send(new GetSignalsQuery(), cancellation)).Select(entry => new SignalListItem(entry.Name, entry.Description, entry.Version, entry.Error, entry.Id, entry.Strategies, entry.Output, entry.Label is { } label ? LabelOf(label) : null)).ToArray();
    }

    [HttpGet("{name}", Name = "GetSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SignalResponse>> Get(string name, CancellationToken cancellation)
    {
        return await Answer(async () => SignalResponses.Of(await sender.Send(new GetSignalQuery(name), cancellation)));
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
            SignalResponses.Of(await sender.Send(new CreateSignalCommand(request.Name ?? "", request.Description, graph, request.Layout ?? NoLayout), cancellation)));
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

        return await Answer(async () => SignalResponses.Of(await sender.Send(new ReplaceSignalCommand(name, request.Description, graph, request.Layout ?? NoLayout), cancellation)));
    }

    [HttpPost("{name}/rename", Name = "RenameSignal")]
    [ProducesResponseType<SignalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SignalResponse>> Rename(string name, RenameSignalRequest request, CancellationToken cancellation)
    {
        return await Answer(async () => SignalResponses.Of(await sender.Send(new RenameSignalCommand(name, request.To ?? ""), cancellation)));
    }

    [HttpDelete("{name}", Name = "DeleteSignal")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<SignalInUseResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(string name, CancellationToken cancellation)
    {
        ActionResult<bool> answer = await Answer(async () =>
        {
            await sender.Send(new DeleteSignalCommand(name), cancellation);
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
                SignalProblem.InUse => Conflict(new SignalInUseResponse([new RequestError("SignalInUse", exception.Message)], exception.Strategies)),
                _ => throw new InvalidOperationException($"Unknown problem {exception.Problem}.", exception)
            };
        }
    }

    /// <summary>
    ///     Sets the label the signal puts on ideas (change add-ideas-and-market, spec idea-labels «Метка сигнала»): a text of 1
    ///     to 12 characters not taken by another label that is on, a timeframe of the five, and named pairs that are listed.
    /// </summary>
    [HttpPut("{name}/label", Name = "SetSignalLabel")]
    [ProducesResponseType<SignalLabelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SignalLabelResponse>> SetLabel(string name, SignalLabelRequest request, CancellationToken cancellation)
    {
        try
        {
            return await Answer(async () => LabelOf(await sender.Send(new SetSignalLabelCommand(name, request.Enabled, request.Text, request.Timeframe), cancellation)));
        }
        catch (IdeasRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Errors.Select(RequestErrors.Of)]));
        }
    }

    private static SignalLabelResponse LabelOf(SignalLabel label)
    {
        return new SignalLabelResponse(label.Enabled, label.Text, Timeframes.Name(label.Timeframe));
    }

    private BadRequestObjectResult MissingGraph()
    {
        return BadRequest(new ErrorResponse([new RequestError("MissingField", "the graph is required") { Field = "graph" }]));
    }
}
