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

/** The definition with the wires and saved positions of the canvas; everything else stays as it was. */
export function fromFlow<T extends GraphDef>(
  definition: T,
  nodes: readonly GraphFlowNode[],
  edges: readonly GraphFlowEdge[],
): T {
  const placed = nodes.filter((node) => node.data.placed);
  const result: T = { ...definition };

  if (definition.wires !== undefined || edges.length > 0) {
    Object.assign(result, { wires: edges.map(toWire) });
  }

  if (definition.layout !== undefined || placed.length > 0) {
    Object.assign(result, {
      layout: Object.fromEntries(
        placed.map((node) => [node.id, { x: node.position.x, y: node.position.y }]),
      ),
    });
  }

  return result;
}

export function edgeId(wire: WireDef): string {
  return `${wire.from}->${wire.to}`;
}

function toEdge(wire: WireDef): GraphFlowEdge {
  const [source = '', sourceHandle = ''] = wire.from.split('.');
  const [target = '', targetHandle = ''] = wire.to.split('.');

  return { id: edgeId(wire), source, sourceHandle, target, targetHandle };
}

function toWire(edge: GraphFlowEdge): WireDef {
  return { from: `${edge.source}.${edge.sourceHandle}`, to: `${edge.target}.${edge.targetHandle}` };
}
