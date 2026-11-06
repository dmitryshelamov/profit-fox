namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The version of the rules ideas are made by and their signals tracked by: the setups, the zones, the rules of the types and the path of a
///     signal (decision 10 of tune-idea-rules). The statistics count only signals of the current version, and a new one replays history again;
///     an exit rule is no part of it.
/// </summary>
public sealed record IdeasEngineVersion(int Number)
{
    /// <summary>
    ///     Raised with every change that changes the signals; the reference test of the version fails until it is. The rows of
    ///     <c>frontend/e2e/seed.ts</c> carry the version too: raise it there with this one.
    /// </summary>
    public const int Current = 5;
}