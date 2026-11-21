using ProfitFox.Domain.Ledger;

namespace ProfitFox.Application.Runs;

/// <summary>
///     What a run decided under the lock of the ledger: its line, and the text of its report when it has one (a run that
///     failed has none).
/// </summary>
public sealed record Decision(LedgerEntry Entry, string? Report);
