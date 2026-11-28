using MediatR;

using ProfitFox.Application.Contracts.Trades;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Services.Trades;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Queries.Trades;

/// <summary>
///     The count of an entry for the dialog (change take-idea-trades, design.md, decision 7; spec idea-trades «Расчёт перед
///     входом»): the numbers and every check, given whole whether the checks pass or not. An empty account or amount is taken
///     from the settings. Nothing is written but the margin mode of the account Bybit named.
/// </summary>
public sealed record PreviewEntryQuery(Guid IdeaId, Guid? AccountId, decimal? Amount) : IRequest<EntryPreviewInternal>;

internal sealed class PreviewEntryQueryHandler(EntryContextReader reader) : IRequestHandler<PreviewEntryQuery, EntryPreviewInternal>
{
    /// <exception cref="KeyNotFoundException">There is no idea or no account of the id.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer.</exception>
    /// <exception cref="ExchangeException">Bybit refused a read for another reason.</exception>
    public async Task<EntryPreviewInternal> Handle(PreviewEntryQuery request, CancellationToken cancellationToken)
    {
        EntryContext context = await reader.ReadAsync(request.IdeaId, request.AccountId, request.Amount, cancellationToken);
        EntryCalculation calculation = EntryCalculator.Count(context.Input);
        return new EntryPreviewInternal(calculation, context.Account?.Id, context.Account?.Name, context.Input.Account?.Free);
    }
}
