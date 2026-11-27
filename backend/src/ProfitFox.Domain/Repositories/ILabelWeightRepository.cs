using ProfitFox.Domain.ValueObjects.Scoring;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where the weights of the labels are kept (change add-idea-score, design.md, decision 1): a row by the text of a label;
///     a text without a row weighs 0. The weights of a label whose text changes move with it in the same transaction as the
///     label (<see cref="ISignalLabelRepository.SetAsync" />).
/// </summary>
public interface ILabelWeightRepository
{
    /// <summary>
    ///     Every row of weights, by text.
    /// </summary>
    Task<IReadOnlyList<LabelWeight>> AllAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the weights of the text, replacing those it had.
    /// </summary>
    Task SetAsync(LabelWeight weight, CancellationToken cancellationToken);

    /// <summary>
    ///     Moves the weights of <paramref name="from" /> to <paramref name="to" />: <paramref name="to" /> takes them when it
    ///     has no row, and keeps its own when it has; <paramref name="from" /> loses its row unless a label of a signal still has
    ///     that text.
    /// </summary>
    Task MoveAsync(string from, string to, CancellationToken cancellationToken);

    /// <summary>
    ///     A row a text of the labels of signals, with its weights, by text; the built-in label is not here.
    /// </summary>
    Task<IReadOnlyList<ScoringLabel>> LabelsAsync(CancellationToken cancellationToken);
}
