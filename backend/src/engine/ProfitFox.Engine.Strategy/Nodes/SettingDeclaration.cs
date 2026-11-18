namespace ProfitFox.Engine.Strategy.Nodes;

/// <summary>
///     A setting a kind of node declares: its name and allowed values. A setting without a default value is required.
/// </summary>
internal sealed record SettingDeclaration(string Name, IReadOnlyList<string> Values, string? Default);