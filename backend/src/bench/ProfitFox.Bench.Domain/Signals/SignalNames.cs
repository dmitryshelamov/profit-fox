using System.Text.RegularExpressions;

namespace ProfitFox.Bench.Domain.Signals;

/// <summary>
///     The rule for the name of a signal, which is also its file name: lower-case latin letters and digits joined by
///     single dashes, at most 64 characters. Only lower case, because the file system of a Mac does not tell
///     <c>Funding.json</c> from <c>funding.json</c>.
/// </summary>
public static partial class SignalNames
{
    public const int MaxLength = 64;

    public static bool IsValid(string? name)
    {
        return name is { Length: > 0 and <= MaxLength } && Pattern().IsMatch(name);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();
}
