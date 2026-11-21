using MediatR;

using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Application.Commands.Runs.EnqueueRun;

/// <summary>
///     Puts a run of the version <see cref="Version" /> of a hypothesis (its latest when null) on a period in the queue (spec
///     bench-run-queue «Постановка в очередь»).
/// </summary>
public sealed record EnqueueRunCommand(string Hypothesis, int? Version, Period Period, bool Parity) : IRequest<RunInternal>;
