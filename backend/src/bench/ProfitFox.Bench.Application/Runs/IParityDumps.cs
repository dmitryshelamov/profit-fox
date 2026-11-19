namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     Where a run writes what it read and computed for the check against the Python bench.
/// </summary>
public interface IParityDumps
{
    IParityDump Open(string folder);
}
