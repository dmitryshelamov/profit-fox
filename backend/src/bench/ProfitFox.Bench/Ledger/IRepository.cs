namespace ProfitFox.Bench.Ledger;

/// <summary>
///     The repository the bench runs in: a verdict is written only for code that is committed.
/// </summary>
public interface IRepository
{
    /// <summary>
    ///     The files changed and not committed, except what the bench writes itself (the ledger and the reports).
    /// </summary>
    IReadOnlyList<string> Uncommitted();

    string Commit();
}
