using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Templates;

/// <summary>A strategy the owner can start from instead of an empty canvas: the definition has no layout.</summary>
/// <param name="Kind"></param>
/// <param name="Name"></param>
/// <param name="Description">The idea of the strategy in one phrase.</param>
/// <param name="Definition"></param>
public sealed record StrategyTemplate(StrategyTemplateKind Kind, string Name, string Description, StrategyDefinition Definition);