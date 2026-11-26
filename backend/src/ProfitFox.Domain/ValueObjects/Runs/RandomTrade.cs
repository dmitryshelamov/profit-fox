using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     A random entry and its outcome (spec strategy-backtest «Случайные входы»): a cycle opened outside the graph on the pair
///     <see cref="Instrument" /> at the moment <see cref="Entry" /> of an order of another pair, on the side of that order.
/// </summary>
public sealed record RandomTrade(string Instrument, DateTimeOffset Entry, EntrySide Side, TradeOutcome Outcome);