namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The version of the rules ideas are made and closed by: the setups, the zones, the rules of the types and the outcome (decision 6). The
///     statistics count only cases of the current version, and a new one replays history again.
/// </summary>
public sealed record IdeasEngineVersion(int Number)
{
    /// <summary>Raised with every change that changes the cases; the reference test of the version fails until it is.</summary>
    public const int Current = 3;
}