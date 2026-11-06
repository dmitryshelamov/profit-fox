using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Application.Ideas;

/// <summary>
///     Codes of setups (<c>impulse</c>, <c>early_impulse</c>, <c>drop</c>, <c>early_drop</c>, <c>high_volatility</c>, <c>breakout</c>,
///     <c>false_breakout</c>, <c>squeeze</c>, <c>exhaustion</c>, <c>rsi_extreme</c>, <c>new_extreme</c>, <c>quiet_volume</c>), idea types
///     (<c>levels</c>, <c>target_2r</c>, <c>beyond_extreme</c>, <c>pullback</c>, <c>stop_entry</c>), sides (<c>long</c>, <c>short</c>) and statuses (<c>awaiting_entry</c>,
///     <c>active</c>, <c>target</c>, <c>stop</c>, <c>expired</c>) in the API and in stored rows.
/// </summary>
public static class IdeaCodes
{
    private static readonly Dictionary<string, IdeaSetup> SetupsByCode = new Dictionary<string, IdeaSetup>
    {
        ["impulse"] = IdeaSetup.Impulse,
        ["early_impulse"] = IdeaSetup.EarlyImpulse,
        ["drop"] = IdeaSetup.Drop,
        ["early_drop"] = IdeaSetup.EarlyDrop,
        ["high_volatility"] = IdeaSetup.HighVolatility,
        ["breakout"] = IdeaSetup.Breakout,
        ["false_breakout"] = IdeaSetup.FalseBreakout,
        ["squeeze"] = IdeaSetup.Squeeze,
        ["exhaustion"] = IdeaSetup.Exhaustion,
        ["rsi_extreme"] = IdeaSetup.RsiExtreme,
        ["new_extreme"] = IdeaSetup.NewExtreme,
        ["quiet_volume"] = IdeaSetup.QuietVolume
    };

    private static readonly Dictionary<string, IdeaType> TypesByCode = new Dictionary<string, IdeaType>
    {
        ["levels"] = IdeaType.Levels,
        ["target_2r"] = IdeaType.TwoR,
        ["beyond_extreme"] = IdeaType.BeyondExtreme,
        ["pullback"] = IdeaType.Pullback,
        ["stop_entry"] = IdeaType.StopEntry
    };

    private static readonly Dictionary<string, IdeaSide> SidesByCode = new Dictionary<string, IdeaSide>
    {
        ["long"] = IdeaSide.Long,
        ["short"] = IdeaSide.Short
    };

    private static readonly Dictionary<string, IdeaStatus> StatusesByCode = new Dictionary<string, IdeaStatus>
    {
        ["awaiting_entry"] = IdeaStatus.AwaitingEntry,
        ["active"] = IdeaStatus.Active,
        ["target"] = IdeaStatus.Target,
        ["stop"] = IdeaStatus.Stop,
        ["expired"] = IdeaStatus.Expired
    };

    public static bool TryParseSetup(string code, out IdeaSetup setup)
    {
        return SetupsByCode.TryGetValue(code, out setup);
    }

    public static bool TryParseType(string code, out IdeaType type)
    {
        return TypesByCode.TryGetValue(code, out type);
    }

    public static bool TryParseSide(string code, out IdeaSide side)
    {
        return SidesByCode.TryGetValue(code, out side);
    }

    public static IdeaStatus ParseStatus(string code)
    {
        return StatusesByCode[code];
    }

    public static string ToCode(this IdeaSetup setup)
    {
        return SetupsByCode.Single(pair => pair.Value == setup).Key;
    }

    public static string ToCode(this IdeaType type)
    {
        return TypesByCode.Single(pair => pair.Value == type).Key;
    }

    public static string ToCode(this IdeaSide side)
    {
        return SidesByCode.Single(pair => pair.Value == side).Key;
    }

    public static string ToCode(this IdeaStatus status)
    {
        return StatusesByCode.Single(pair => pair.Value == status).Key;
    }
}