using ProfitFox.Bench.Domain.Ledger;

namespace ProfitFox.Bench.Application.Runs;

/// <summary>
///     The dump of one run: pair by pair what was read and computed, then the block means and the line of the ledger.
///     Event outcomes are written only when the run computed them.
/// </summary>
public interface IParityDump : IDisposable
{
    void Write(PairData data, bool withEventOutcomes);

    void Finish(
        IReadOnlyList<(long Block, double Mean, int Events)> pseudo,
        IReadOnlyList<(long Block, double Mean, int Events)>? blocks,
        LedgerEntry entry);
}
