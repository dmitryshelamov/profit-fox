import type { Edge, Node } from '@xyflow/react';

import { layoutGraph } from './layout';
import type { AnyDef, CatalogNode, GraphNodeDef, WireDef } from './types';

export interface GraphNodeData extends Record<string, unknown> {
  readonly node: GraphNodeDef;
  /** Whether the position is saved in the layout; other nodes are placed on the canvas only. */
  readonly placed: boolean;
}

export type GraphFlowNode = Node<GraphNodeData, 'graph'>;
export type GraphFlowEdge = Edge;

/**
 * The canvas of a definition: a node per graph node and an edge per wire, from output pin to input pin. A node stands
 * at its saved position, the others where the layout by stages puts them.
 */
export function toFlow(
  definition: AnyDef,
  catalog: readonly CatalogNode[],
): { nodes: GraphFlowNode[]; edges: GraphFlowEdge[] } {
  const layout = definition.layout ?? {};
  const staged = layoutGraph(definition, catalog);

  const nodes = definition.nodes.map((node): GraphFlowNode => {
    const saved = layout[node.id];
    const position = saved ?? staged[node.id]!;

    return { id: node.id, type: 'graph', position, data: { node, placed: saved !== undefined } };
  });

  return { nodes, edges: (definition.wires ?? []).map(toEdge) };
}

function edgeId(wire: WireDef): string {
  return `${wire.from}->${wire.to}`;
}

function toEdge(wire: WireDef): GraphFlowEdge {
  const [source = '', sourceHandle = ''] = wire.from.split('.');
  const [target = '', targetHandle = ''] = wire.to.split('.');

  return { id: edgeId(wire), source, sourceHandle, target, targetHandle };
}
