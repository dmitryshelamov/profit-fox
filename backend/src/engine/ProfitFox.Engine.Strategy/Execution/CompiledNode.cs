using ProfitFox.Engine.Strategy.Graph.Model;
using ProfitFox.Engine.Strategy.Nodes;

namespace ProfitFox.Engine.Strategy.Execution;

internal sealed record CompiledNode(NodeDefinition Definition, NodeKind Kind, InputSource[] Inputs, int[] OutputSlots);
