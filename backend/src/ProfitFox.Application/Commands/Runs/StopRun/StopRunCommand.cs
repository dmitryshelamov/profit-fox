using MediatR;

using ProfitFox.Application.Commands.Runs.Base;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Application.Commands.Runs.StopRun;

/// <summary>
///     Ends a run of the queue without computing it: with <see cref="RunStatus.Failed" /> for a run that was interrupted too
///     often, with <see cref="RunStatus.Cancelled" /> for a run its owner stopped. The line has no report.
/// </summary>
public sealed record StopRunCommand(
    string Hypothesis,
    int? Version,
    Period Period,
    bool Parity,
    DateTimeOffset Today,
    RunAssignment? Assignment,
    string Status,
    string Reason) : BaseRunCommand(Hypothesis, Version, Period, Parity, Today, Assignment), IRequest;
