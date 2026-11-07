using ProfitFox.Domain.Entities.IdeaAutos.Enums;

namespace ProfitFox.Application.IdeaAutos;

/// <summary>
///     Codes of the modes of an auto in the API and in stored rows, <c>off</c>, <c>paper</c>, <c>live</c>, and of its conditions by a feature,
///     <c>any</c>, <c>with</c>, <c>without</c>.
/// </summary>
public static class IdeaAutoCodes
{
    private static readonly Dictionary<string, IdeaAutoMode> ModesByCode = new Dictionary<string, IdeaAutoMode>(StringComparer.Ordinal)
    {
        ["off"] = IdeaAutoMode.Off,
        ["paper"] = IdeaAutoMode.Paper,
        ["live"] = IdeaAutoMode.Live
    };

    private static readonly Dictionary<string, FeatureCondition> FeatureConditionsByCode = new Dictionary<string, FeatureCondition>(StringComparer.Ordinal)
    {
        ["any"] = FeatureCondition.Any,
        ["with"] = FeatureCondition.With,
        ["without"] = FeatureCondition.Without
    };

    public static bool TryParseFeatureCondition(string code, out FeatureCondition condition)
    {
        return FeatureConditionsByCode.TryGetValue(code, out condition);
    }

    public static string ToCode(this FeatureCondition condition)
    {
        return FeatureConditionsByCode.Single(pair => pair.Value == condition).Key;
    }

    public static bool TryParseMode(string code, out IdeaAutoMode mode)
    {
        return ModesByCode.TryGetValue(code, out mode);
    }

    public static string ToCode(this IdeaAutoMode mode)
    {
        return ModesByCode.Single(pair => pair.Value == mode).Key;
    }
}