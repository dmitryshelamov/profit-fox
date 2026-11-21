namespace ProfitFox.Presentation.Contracts.Nodes;

/// <summary>
///     A setting of a node kind and the values it may take.
/// </summary>
public sealed record SettingResponse(string Name, IReadOnlyList<string> Values, string? Default);
