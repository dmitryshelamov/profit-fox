import type { Edge, Node } from '@xyflow/react';

import type { GraphDef, GraphNodeDef, Position, WireDef } from './types';

export interface GraphNodeData extends Record<string, unknown> {
  readonly node: GraphNodeDef;
  /** Whether the position is saved in the layout; other nodes are placed on the canvas only. */
  readonly placed: boolean;
}

export type GraphFlowNode = Node<GraphNodeData, 'graph'>;
export type GraphFlowEdge = Edge;

const column = 240;
const row = 200;
const perRow = 4;

/** The canvas of a definition: a node per graph node and an edge per wire, from output pin to input pin. */
export function toFlow(definition: GraphDef): { nodes: GraphFlowNode[]; edges: GraphFlowEdge[] } {
  const layout = definition.layout ?? {};
  const below = Math.max(0, ...Object.values(layout).map((position) => position.y)) + row;
  let unplaced = 0;

  const nodes = definition.nodes.map((node): GraphFlowNode => {
    const saved = layout[node.id];
    const position: Position = saved ?? {
      x: (unplaced % perRow) * column,
      y: below + Math.floor(unplaced++ / perRow) * row,
    };

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
