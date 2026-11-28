using MediatR;

using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Scoring;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Scoring;

namespace ProfitFox.Application.Commands.Scoring.SetLabelWeight;

/// <summary>
///     Both weights are checked first, every error at its field; then the text must be one of a label of a signal — on or
///     off — or the built-in label (design.md, decision 1: a row of weights without a label is not shown, so none is made).
/// </summary>
internal sealed class SetLabelWeightCommandHandler(ILabelWeightRepository weights, ISignalLabelRepository labels)
    : IRequestHandler<SetLabelWeightCommand, ScoringLabel>
{
    /// <exception cref="IdeasRequestException">A weight is not a number from −100 to +100 with at most two digits after the point.</exception>
    /// <exception cref="LabelNotFoundException">There is no label with the text.</exception>
    public async Task<ScoringLabel> Handle(SetLabelWeightCommand request, CancellationToken cancellationToken)
    {
        List<PreviewErrorInternal> errors = [];
        Check(errors, "longWeight", request.LongWeight);
        Check(errors, "shortWeight", request.ShortWeight);
        if (errors.Count > 0)
        {
            throw new IdeasRequestException(errors);
        }

        string text = request.Text ?? "";
        if (text != IdeasSettings.BurstLabel && !(await labels.AllAsync(cancellationToken)).Any(label => label.Text == text))
        {
            throw new LabelNotFoundException(text);
        }

        await weights.SetAsync(new LabelWeight(text, request.LongWeight!.Value, request.ShortWeight!.Value), cancellationToken);
        return (await GetScoringLabelsQueryHandler.RowsAsync(weights, cancellationToken)).First(row => row.Text == text);
    }

    // The text of the error is that of change decimal-label-weights, design.md, decision 2.
    private static void Check(List<PreviewErrorInternal> errors, string field, decimal? weight)
    {
        if (weight is not { } value || !LabelWeight.Valid(value))
        {
            errors.Add(new PreviewErrorInternal("InvalidWeight", "вес от −100 до +100, не больше двух знаков после точки") { Field = field });
        }
    }
}
