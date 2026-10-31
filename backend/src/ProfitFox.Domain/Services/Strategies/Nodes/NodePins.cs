using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Nodes;

public sealed record NodePins(IReadOnlyList<PinDeclaration> Inputs, IReadOnlyList<PinDeclaration> Outputs);