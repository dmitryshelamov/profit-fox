namespace ProfitFox.Bench.Domain.Runs;

/// <summary>
///     The parts of history a hypothesis is checked on (spec hypothesis-bench).
/// </summary>
public enum Period
{
    Exploration,
    Holdout,
    NewData
}
