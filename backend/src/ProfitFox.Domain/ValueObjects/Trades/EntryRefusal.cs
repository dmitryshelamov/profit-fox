namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     Why the store refused to write an entry after counting the limits anew under the lock of the entry: the check that
///     failed, as <see cref="EntryCheck" /> names it.
/// </summary>
public sealed record EntryRefusal(EntryCheck Check);
