namespace ProfitFox.Domain.Ledger;

/// <summary>
///     The rules of the ledger of runs (spec hypothesis-bench) over its lines, by hypothesis version: a verdict of a period
///     is final for its version, the holdout opens once and only after the version passed exploration, and the attempts the
///     required t grows with are counted. A line corrected by a later one is out of the rules.
/// </summary>
public static class LedgerRules
{
    /// <summary>
    ///     Refuses a run of version <paramref name="version" /> of <paramref name="hypothesis" /> on <paramref name="period" />
    ///     that the rules of the ledger do not allow. A parity run is blind and is allowed on exploration only.
    /// </summary>
    public static void CheckCanRun(IReadOnlyList<LedgerEntry> ledger, string hypothesis, int version, string period, bool parity)
    {
        if (parity)
        {
            if (period != "exploration")
            {
                throw new LedgerException($"A parity run reads only exploration, not {period}.");
            }

            return;
        }

        IReadOnlyList<LedgerEntry> entries = [.. ledger.Where(entry => entry.Hypothesis == hypothesis && entry.Version == version && !IsCorrected(ledger, entry))];
        LedgerEntry? final = entries.FirstOrDefault(entry => entry.Period == period && RunStatus.IsFinal(entry.Status));
        if (final is not null)
        {
            throw new LedgerException($"{hypothesis} version {version} already has the verdict {final.Status} on {period} (run {final.RunId}).", final.RunId);
        }

        if (period == "holdout" && !entries.Any(entry => entry.Period == "exploration" && entry.Status == RunStatus.Passed))
        {
            throw new LedgerException($"The holdout opens only after {hypothesis} version {version} passed exploration.");
        }
    }

    /// <summary>
    ///     The attempts of a period: the different versions of hypotheses with a final verdict in it, this one included.
    /// </summary>
    public static int Attempts(IReadOnlyList<LedgerEntry> ledger, string hypothesis, int version, string period)
    {
        HashSet<(string, int)> tried = [.. ledger
            .Where(entry => entry.Period == period && RunStatus.IsFinal(entry.Status) && !IsCorrected(ledger, entry))
            .Select(entry => (entry.Hypothesis, entry.Version))];
        tried.Add((hypothesis, version));
        return tried.Count;
    }

    private static bool IsCorrected(IReadOnlyList<LedgerEntry> ledger, LedgerEntry entry)
    {
        return ledger.Any(other => other.Corrects == entry.RunId);
    }
}
