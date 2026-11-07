using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.Services.Ideas;

/// <summary>
///     The types of ideas of each setup (decision 6): a setup of a range only waits for its way out, a setup against the move has no move to
///     pull back into, the rest go at the market or pull back.
/// </summary>
public static class SetupTypes
{
    private static readonly IdeaType[] Range = [IdeaType.StopEntry];
    private static readonly IdeaType[] Against = [IdeaType.Levels, IdeaType.TwoR, IdeaType.BeyondExtreme];
    private static readonly IdeaType[] Move = [IdeaType.Levels, IdeaType.TwoR, IdeaType.BeyondExtreme, IdeaType.Pullback];

    public static IReadOnlyList<IdeaType> Of(IdeaSetup setup)
    {
        return setup switch
        {
            IdeaSetup.Squeeze or IdeaSetup.QuietVolume or IdeaSetup.Accumulation => Range,
            IdeaSetup.Exhaustion or IdeaSetup.RsiExtreme => Against,
            _ => Move
        };
    }
}