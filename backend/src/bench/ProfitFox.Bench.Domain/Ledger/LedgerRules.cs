namespace ProfitFox.Bench.Domain.Ledger;

/// <summary>
///     The rules of the ledger of runs (spec hypothesis-bench) over its lines: a hypothesis id keeps one hash, a verdict
///     of a period is final, the holdout opens once and only after the hypothesis passed exploration, and the attempts
///     the required t grows with are counted. A line corrected by a later one is out of the rules.
/// </summary>
public static class LedgerRules
{
    /// <summary>
    ///     Refuses a run of <paramref name="hypothesis" /> with <paramref name="hash" /> on <paramref name="period" /> that the
    ///     rules of the ledger do not allow. A parity run is blind and only needs the hash to match.
    /// </summary>
    public static void CheckCanRun(IReadOnlyList<LedgerEntry> ledger, string hypothesis, string hash, string period, bool parity)
    {
        IReadOnlyList<LedgerEntry> entries = [.. ledger.Where(entry => entry.Hypothesis == hypothesis && !IsCorrected(ledger, entry))];
        LedgerEntry? other = entries.FirstOrDefault(entry => entry.Hash != hash);
        if (other is not null)
        {
            throw new LedgerException(
                $"{hypothesis} was run with another file (hash {other.Hash[..12]}…, run {other.RunId}); a changed hypothesis is a new hypothesis with a new id.");
        }

        if (parity)
        {
            return;
        }

        LedgerEntry? final = entries.FirstOrDefault(entry => entry.Period == period && RunStatus.IsFinal(entry.Status));
        if (final is not null)
        {
            throw new LedgerException($"{hypothesis} already has the verdict {final.Status} on {period} (run {final.RunId}).");
        }

        if (period == "holdout" && !entries.Any(entry => entry.Period == "exploration" && entry.Status == RunStatus.Passed))
        {
            throw new LedgerException($"The holdout opens only after {hypothesis} passed exploration.");
        }
    }

    /// <summary>
    ///     The attempts of a period: the different hypotheses with a final verdict in it, this one included.
    /// </summary>
    public static int Attempts(IReadOnlyList<LedgerEntry> ledger, string hypothesis, string period)
    {
        HashSet<string> tried = [.. ledger
            .Where(entry => entry.Period == period && RunStatus.IsFinal(entry.Status) && !IsCorrected(ledger, entry))
            .Select(entry => entry.Hypothesis)];
        tried.Add(hypothesis);
        return tried.Count;
    }

    private static bool IsCorrected(IReadOnlyList<LedgerEntry> ledger, LedgerEntry entry)
    {
        return ledger.Any(other => other.Corrects == entry.RunId);
    }
}
