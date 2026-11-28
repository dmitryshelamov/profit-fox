using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Contracts.Trades;

/// <summary>
///     The count of an entry with the account it was counted for — null when there is none — and its free USDT as Bybit gave
///     them, null when they could not be read (spec idea-trades «Расчёт перед входом»).
/// </summary>
public sealed record EntryPreviewInternal(EntryCalculation Calculation, Guid? AccountId, string? AccountName, decimal? Free);
