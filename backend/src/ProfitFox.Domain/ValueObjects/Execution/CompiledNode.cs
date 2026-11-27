using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Nodes;

namespace ProfitFox.Domain.ValueObjects.Execution;

internal sealed record CompiledNode(NodeDefinition Definition, NodeKind Kind, InputSource[] Inputs, int[] OutputSlots);
