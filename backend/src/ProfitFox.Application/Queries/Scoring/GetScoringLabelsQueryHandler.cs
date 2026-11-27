using MediatR;

using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Scoring;

namespace ProfitFox.Application.Queries.Scoring;

/// <summary>
///     The labels of signals come from the store with their weights; the built-in label «⚡ всплеск» is added last, on, without
///     a timeframe or a signal, with the weights of its text.
/// </summary>
internal sealed class GetScoringLabelsQueryHandler(ILabelWeightRepository weights) : IRequestHandler<GetScoringLabelsQuery, IReadOnlyList<ScoringLabel>>
{
    public async Task<IReadOnlyList<ScoringLabel>> Handle(GetScoringLabelsQuery request, CancellationToken cancellationToken)
    {
        return await RowsAsync(weights, cancellationToken);
    }

    /// <summary>
    ///     The rows of the page: the labels of signals, then the built-in label.
    /// </summary>
    public static async Task<IReadOnlyList<ScoringLabel>> RowsAsync(ILabelWeightRepository weights, CancellationToken cancellationToken)
    {
        IReadOnlyList<ScoringLabel> labels = await weights.LabelsAsync(cancellationToken);
        LabelWeight? burst = (await weights.AllAsync(cancellationToken)).FirstOrDefault(weight => weight.Text == IdeasSettings.BurstLabel);
        return
        [
            .. labels.Where(label => label.Text != IdeasSettings.BurstLabel),
            new ScoringLabel(IdeasSettings.BurstLabel, Enabled: true, Timeframe: null, Signal: null, burst?.Long ?? 0, burst?.Short ?? 0, BuiltIn: true)
        ];
    }
}
