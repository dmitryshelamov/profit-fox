using ProfitFox.Bench.Application.Runs;

namespace ProfitFox.Bench.Infrastructure.Runs;

/// <summary>
///     Parity dumps as gzip CSV files in a folder (<see cref="ParityDump" />).
/// </summary>
public sealed class ParityDumpFiles : IParityDumps
{
    public IParityDump Open(string folder)
    {
        return new ParityDump(folder);
    }
}
