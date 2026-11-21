namespace ProfitFox.Domain.Nodes;

/// <summary>
///     A setting a node kind declares: its allowed values and, when it may be left out, its default.
/// </summary>
public sealed record SettingDeclaration(string Name, IReadOnlyList<string> Values, string? Default);