namespace ProfitFox.Bench.Domain.Runs;

/// <summary>
///     The version of the engine (spec hypothesis-bench «Номер версии движка»): the line of the ledger and the report of a
///     run carry it. It grows with any change of the events, the outcomes of trades or the verdict on the same data; the
///     reference test of H0001 and of the hypothesis with overlapping events fails until it does.
///     <list type="bullet">
///         <item>1: every event of a signal is a trade of its own.</item>
///         <item>
///             2: a hypothesis is executed as a strategy with one position per pair; an event that comes while the position
///             is open is skipped (change add-strategy-graph). The results of H0001 are those of version 1.
///         </item>
///     </list>
/// </summary>
public static class EngineVersion
{
    public const int Number = 2;
}
