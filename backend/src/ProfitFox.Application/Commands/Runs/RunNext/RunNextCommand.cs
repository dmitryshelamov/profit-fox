using MediatR;

namespace ProfitFox.Application.Commands.Runs.RunNext;

/// <summary>
///     Takes the run of the queue that was queued first and goes it to the end (spec bench-run-queue), as the Worker of
///     <see cref="Settings" />: under its name, with its lease.
/// </summary>
public sealed record RunNextCommand(WorkerSettings Settings) : IRequest<RunNextResult>;
