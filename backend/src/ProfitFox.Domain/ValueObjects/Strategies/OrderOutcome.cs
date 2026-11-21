using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>
///     An entry order of a strategy and what became of it (spec strategy-preview «Исход заявки»): its moment and side, its
///     result, why it gave no trade when it did not, and the trade it opened.
/// </summary>
public sealed record OrderOutcome(DateTimeOffset Moment, EntrySide Side, OrderResult Result, OrderExclusion? Exclusion, TradeOutcome? Trade);
