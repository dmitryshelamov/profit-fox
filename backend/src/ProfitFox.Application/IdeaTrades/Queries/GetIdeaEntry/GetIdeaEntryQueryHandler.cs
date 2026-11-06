using MediatR;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaEntry;

public sealed class GetIdeaEntryQueryHandler(IdeaTradeEntryCalculator calculator) : IRequestHandler<GetIdeaEntryQuery, EntryCalculation?>
{
    public async Task<EntryCalculation?> Handle(GetIdeaEntryQuery request, CancellationToken cancellationToken)
    {
        EntryPreparation? preparation = await calculator.PrepareAsync(request.IdeaId, request.AccountId, request.Amount, cancellationToken);

        return preparation is null ? null : await calculator.EvaluateAsync(preparation, null, cancellationToken);
    }
}