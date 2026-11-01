using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies;

/// <summary>One setting of a simple node, as a definition writes it under <c>settings</c> and the catalog describes it.</summary>
/// <param name="Name">The property under <c>settings</c>, such as <c>side</c>.</param>
/// <param name="Values">The values a definition may give, in the order the editor offers them.</param>
/// <param name="Default">The value the editor gives a new node.</param>
/// <param name="Required">Whether a definition must give the setting.</param>
/// <param name="TypedInputs">The inputs whose type the setting chooses.</param>
/// <param name="Apply">Puts a parsed value into the node settings.</param>
public sealed record NodeSettingSpec(
    string Name,
    IReadOnlyList<Enum> Values,
    Enum Default,
    bool Required,
    IReadOnlyList<string> TypedInputs,
    Func<NodeSettings, Enum, NodeSettings> Apply);