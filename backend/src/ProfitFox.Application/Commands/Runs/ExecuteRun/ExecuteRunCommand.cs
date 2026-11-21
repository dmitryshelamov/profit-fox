using MediatR;

using ProfitFox.Application.Commands.Runs.Base;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Application.Commands.Runs.ExecuteRun;

/// <summary>
///     Runs a version of a hypothesis (spec hypothesis-bench). A run that is not of the queue is refused by the rules of the
///     ledger before anything is written; a run of the queue (<see cref="BaseRunCommand.Assignment" />) ends with a line in
///     every case, a refusal included, since its row would otherwise never end. <see cref="Progress" /> gets the pairs as they
///     pass.
/// </summary>
public sealed record ExecuteRunCommand(
    string Hypothesis,
    int? Version,
    Period Period,
    bool Parity,
    DateTimeOffset Today,
    RunAssignment? Assignment = null,
    RunProgress? Progress = null) : BaseRunCommand(Hypothesis, Version, Period, Parity, Today, Assignment), IRequest<ExecuteRunResult>;
