using ProfitFox.Domain.Graph.Model;
using ProfitFox.Domain.Nodes;

namespace ProfitFox.Domain.Execution;

internal sealed record CompiledNode(NodeDefinition Definition, NodeKind Kind, InputSource[] Inputs, int[] OutputSlots);
