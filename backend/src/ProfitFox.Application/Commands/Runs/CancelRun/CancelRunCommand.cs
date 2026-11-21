using MediatR;

using ProfitFox.Domain.Entities.Runs.Enums;

namespace ProfitFox.Application.Commands.Runs.CancelRun;

/// <summary>
///     Takes a waiting run out of the queue, or asks the Worker of a going one to stop (spec bench-run-queue «Отмена прогона»).
/// </summary>
public sealed record CancelRunCommand(string RunId) : IRequest<CancelOutcome>;
