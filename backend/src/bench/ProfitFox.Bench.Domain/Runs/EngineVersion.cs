namespace ProfitFox.Bench.Domain.Runs;

/// <summary>
///     The version of the engine (spec hypothesis-bench «Номер версии движка»): the line of the ledger and the report of a
///     run carry it. It grows with any change of the events, the outcomes of trades or the verdict on the same data; the
///     reference test of H0001 fails until it does.
/// </summary>
public static class EngineVersion
{
    public const int Number = 1;
}
