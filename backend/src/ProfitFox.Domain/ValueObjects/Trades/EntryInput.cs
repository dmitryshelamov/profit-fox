using ProfitFox.Domain.Entities.Ideas;

namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The account of an entry as the exchange shows it now: why its key is refused — null when it is taken —, its margin
///     mode as Bybit names it, the positions of the pair on it, and its free USDT. What could not be read is null.
/// </summary>
public sealed record EntryAccount(Guid Id, string Name, string? KeyProblem, string? MarginMode, IReadOnlyList<ExchangePosition> Positions, decimal? Free);

/// <summary>
///     What the base tells of the entry: whether the emergency stop holds, whether a trading Worker works, the trades that
///     hold their pairs and the sum of their amounts, and whether a trade holds the pair on the account.
/// </summary>
public sealed record EntryCounters(bool Halted, bool TraderAlive, int OpenTrades, decimal OpenAmount, bool PairTaken);

/// <summary>
///     Everything an entry is counted from, read anew for each count (design.md, decision 7): the idea, the settings of the
///     risk module, the amount, the account — null when there is none —, the last price of the exchange, the rules of the
///     pair — null when the exchange does not know it —, the counters and the moment.
/// </summary>
public sealed record EntryInput(Idea Idea, TradeSettings Settings, decimal Amount, EntryAccount? Account, decimal Price, PairRules? Rules, EntryCounters Counters, DateTimeOffset Now);
