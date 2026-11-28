using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Exceptions;

/// <summary>
///     The entry was refused (spec idea-trades «Расчёт перед входом»): the checks that did not pass, each with its reason. No
///     trade and no order were written.
/// </summary>
public sealed class TradeEntryRefusedException(IReadOnlyList<EntryCheck> failed)
    : Exception(string.Join("; ", failed.Select(check => check.Text)))
{
    public IReadOnlyList<EntryCheck> Failed { get; } = failed;
}
