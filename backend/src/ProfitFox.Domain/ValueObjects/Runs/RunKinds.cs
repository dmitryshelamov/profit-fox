namespace ProfitFox.Domain.ValueObjects.Runs;

/// <summary>
///     The kinds of a run: with a verdict, or the blind parity run.
/// </summary>
public static class RunKinds
{
    public const string Verdict = "verdict";
    public const string Parity = "parity";

    public static string Name(bool parity)
    {
        return parity ? Parity : Verdict;
    }
}
