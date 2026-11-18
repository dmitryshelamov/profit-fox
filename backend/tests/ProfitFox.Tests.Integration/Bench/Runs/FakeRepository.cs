using ProfitFox.Bench.Ledger;

namespace ProfitFox.Tests.Integration.Bench.Runs;

/// <summary>
///     A repository with the changes the test sets.
/// </summary>
public sealed class FakeRepository : IRepository
{
    public List<string> Changes { get; } = [];

    public IReadOnlyList<string> Uncommitted()
    {
        return Changes;
    }

    public string Commit()
    {
        return "0000000000000000000000000000000000000000";
    }
}
