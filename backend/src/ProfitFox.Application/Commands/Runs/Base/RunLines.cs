using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ledger;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.Ledger;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Application.Commands.Runs.Base;

/// <summary>
///     The line a run writes in the ledger: what it starts as, the check of the rules of the ledger, and the line of a run that
///     ends without a verdict.
/// </summary>
internal static class RunLines
{
    /// <summary>
    ///     The line of a run before it is decided: failed until the ledger decides otherwise.
    /// </summary>
    public static LedgerEntry Line(BaseRunCommand command, Hypothesis hypothesis, string period)
    {
        return new LedgerEntry
        {
            RunId = command.Assignment?.RunId ?? Guid.NewGuid().ToString("N"),
            Time = DateTimeOffset.UtcNow,
            Hypothesis = hypothesis.Id,
            Hash = hypothesis.Hash,
            Version = hypothesis.Version,
            EngineVersion = EngineVersion.Number,
            Period = period,
            Status = RunStatus.Failed
        };
    }

    public static async Task CheckCanRunAsync(ILedgerRepository ledger, Hypothesis hypothesis, string period, bool parity, CancellationToken cancellationToken)
    {
        LedgerRules.CheckCanRun(await ledger.EntriesAsync(cancellationToken), hypothesis.Id, hypothesis.Version, period, parity);
    }

    /// <summary>
    ///     Writes the line of a run that ends without a verdict: it shows the attempts the verdict would have been, and has no
    ///     report.
    /// </summary>
    public static Task CloseAsync(ILedgerRepository ledger, Hypothesis hypothesis, LedgerEntry entry, string status, string reason, RunAssignment? assignment)
    {
        return ledger.DecideAsync(
            entries => new Decision(
                entry with
                {
                    Status = status,
                    Attempts = LedgerRules.Attempts(entries, hypothesis.Id, hypothesis.Version, entry.Period),
                    Reason = reason
                },
                Report: null),
            assignment,
            CancellationToken.None);
    }
}
