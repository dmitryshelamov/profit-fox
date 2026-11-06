using ProfitFox.Domain.Entities.IdeaAutos.Enums;

namespace ProfitFox.Application.IdeaAutos;

/// <summary>Codes of the modes of an auto in the API and in stored rows: <c>off</c>, <c>paper</c>, <c>live</c>.</summary>
public static class IdeaAutoCodes
{
    private static readonly Dictionary<string, IdeaAutoMode> ModesByCode = new Dictionary<string, IdeaAutoMode>(StringComparer.Ordinal)
    {
        ["off"] = IdeaAutoMode.Off,
        ["paper"] = IdeaAutoMode.Paper,
        ["live"] = IdeaAutoMode.Live
    };

    public static bool TryParseMode(string code, out IdeaAutoMode mode)
    {
        return ModesByCode.TryGetValue(code, out mode);
    }

    public static string ToCode(this IdeaAutoMode mode)
    {
        return ModesByCode.Single(pair => pair.Value == mode).Key;
    }
}