using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Nodes;

/// <summary>What the pins of context-dependent nodes come from: strategy parameters and declared pins of a composite node.</summary>
public sealed record PinContext(
    IReadOnlyList<StrategyParameter> Parameters,
    IReadOnlyList<PinDeclaration> CompositeInputs,
    IReadOnlyList<PinDeclaration> CompositeOutputs);