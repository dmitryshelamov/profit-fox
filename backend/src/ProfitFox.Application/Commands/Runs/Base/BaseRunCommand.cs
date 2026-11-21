using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;

namespace ProfitFox.Application.Commands.Runs.Base;

/// <summary>
///     What to run: a version of a hypothesis (the latest when <see cref="Version" /> is null) on a period, blind for the
///     parity check or with a verdict. A run of the queue also has its <see cref="Assignment" />: the run takes its id, and
///     its line is written only while the Worker holds the lease.
/// </summary>
public abstract record BaseRunCommand(string Hypothesis, int? Version, Period Period, bool Parity, DateTimeOffset Today, RunAssignment? Assignment);
