using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>
///     The account a trade enters on: what its key lets it do, the free money in the quote asset, the working bots with their money, the
///     margins of trades that wait for their entry, and the directions of the bots and the open trades on the symbol (<c>linear</c> only).
///     <see cref="Symbol" /> is the symbol of <c>linear</c> as the exchange has it, none when it was not read.
/// </summary>
public sealed record IdeaTradeAccount(
    AccountAccess Access,
    decimal FreeMoney,
    IReadOnlyCollection<BotFunds> Bots,
    IReadOnlyCollection<decimal> PendingEntries,
    IReadOnlyCollection<StrategyDirection> SymbolDirections,
    LinearSymbol? Symbol = null);