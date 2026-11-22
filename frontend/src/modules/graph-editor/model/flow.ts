import type { Edge, Node } from '@xyflow/react';

import { nodeOf, pinOf } from './graph';
import { placesOf } from './layout';
import type { GraphDocument, GraphNode, NodeKind } from './types';
import { backWires } from './wires';

export { backWires };

export type GraphFlowNode = Node<{ node: GraphNode }, 'graph'>;
export type GraphFlowEdge = Edge;

/** The nodes and wires of a document as xyflow draws them: a handle per pin, named by the pin. */
export function toFlow(
  document: GraphDocument,
  catalog: readonly NodeKind[],
): { nodes: GraphFlowNode[]; edges: GraphFlowEdge[] } {
  const places = placesOf(document.graph, document.layout, catalog);
  const back = backWires(document.graph, catalog);
  return {
    nodes: document.graph.nodes.map((node) => ({
      id: node.id,
      type: 'graph',
      position: places[node.id] ?? { x: 0, y: 0 },
      data: { node },
    })),
    // A wire back into the position is drawn as an arc over the nodes.
    edges: document.graph.wires.map((wire) => {
      const id = `${wire.from}->${wire.to}`;
      return {
        id,
        source: nodeOf(wire.from),
        sourceHandle: pinOf(wire.from),
        target: nodeOf(wire.to),
        targetHandle: pinOf(wire.to),
        ...(back.has(id) ? { type: 'back' } : {}),
      };
    }),
  };
}

/** The wire of an edge of the canvas. */
export function wireOf(edge: {
  source: string;
  sourceHandle?: string | null;
  target: string;
  targetHandle?: string | null;
}): { from: string; to: string } {
  return {
    from: `${edge.source}.${edge.sourceHandle ?? ''}`,
    to: `${edge.target}.${edge.targetHandle ?? ''}`,
  };
}
