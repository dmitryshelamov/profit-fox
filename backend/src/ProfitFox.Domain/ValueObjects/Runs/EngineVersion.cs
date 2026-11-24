namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     The version of the engine (spec strategy-backtest «Номер версии движка»): every backtest carries it. It grows with
///     any change of the trades, the random entries or the numbers of the result on the same data; the reference test of
///     the strategies of H0001 and H0002 fails until it does.
///     <list type="bullet">
///         <item>1: every event of a signal is a trade of its own.</item>
///         <item>
///             2: a hypothesis is executed as a strategy with one position per pair; an event that comes while the position
///             is open is skipped (change add-strategy-graph). The results of H0001 are those of version 1.
///         </item>
///         <item>
///             3: a backtest of a strategy instead of a run of a hypothesis (change add-strategy-backtest): no numbers of a
///             verdict, and the seed of random entries comes from the graph of the set and its timeframe. With the hash of
///             the hypothesis as the seed the trades of orders and random entries are those of version 2.
///         </item>
///         <item>
///             4: a funding payment at <c>T</c> belongs to the candle that closes after <c>T</c>, not to the one that closes at
///             <c>T</c> (change add-ideas-and-market, task 27.3): what the graph of a candle reads of the funding is what was
///             known live at its close. What a position pays or receives is not changed: the payments at
///             <c>entry &lt; t ≤ exit</c>.
///         </item>
///     </list>
/// </summary>
public static class EngineVersion
{
    public const int Number = 4;
}
