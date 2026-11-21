namespace ProfitFox.Domain.ValueObjects.Nodes;

/// <summary>
///     A setting a node kind declares: its allowed values and, when it may be left out, its default. A free setting has no
///     list of values: the kind checks the value itself (change add-strategy-constructor, design.md, decision 1).
/// </summary>
public sealed record SettingDeclaration(string Name, IReadOnlyList<string> Values, string? Default)
{
    /// <summary>
    ///     Whether the value is not one of <see cref="Values" />, which is then empty, but any text the kind accepts.
    /// </summary>
    public bool Free { get; init; }
}
