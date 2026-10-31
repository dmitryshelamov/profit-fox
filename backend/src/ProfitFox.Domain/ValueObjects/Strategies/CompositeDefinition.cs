namespace ProfitFox.Domain.ValueObjects.Strategies;

public sealed record CompositeDefinition(IReadOnlyList<PinDeclaration> Inputs, IReadOnlyList<PinDeclaration> Outputs, Graph Graph);