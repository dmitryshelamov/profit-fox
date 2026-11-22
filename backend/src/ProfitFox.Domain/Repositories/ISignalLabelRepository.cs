using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where the labels of saved signals are kept (change add-ideas-and-market, design.md, decision 7); a label goes with its
///     signal, and the text of a label that is on is one of its kind.
/// </summary>
public interface ISignalLabelRepository
{
    Task<SignalLabel?> FindAsync(Guid signalId, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the label of the signal, replacing the one it had; false when another label that is on has the text.
    /// </summary>
    Task<bool> SetAsync(SignalLabel label, CancellationToken cancellationToken);

    Task<IReadOnlyList<SignalLabel>> AllAsync(CancellationToken cancellationToken);
}
