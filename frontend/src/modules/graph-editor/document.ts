import type { Graph, GraphNode, NodeKind } from './model/types';

/**
 * The rules of a kind of document the editor edits. A signal has exactly one output, the event `entry`, and one
 * `outputs` node that is never removed and is not in the palette; a strategy of step 5 brings its own rules.
 */
export interface DocumentRules {
  readonly kind: 'signal';
  /** The graph a new document starts with. */
  readonly start: () => Graph;
  /** The kinds the palette offers. */
  readonly palette: (catalog: readonly NodeKind[]) => NodeKind[];
  readonly canRemove: (node: GraphNode) => boolean;
}

export const signalRules: DocumentRules = {
  kind: 'signal',
  start: () => ({
    formatVersion: 1,
    parameters: [],
    outputs: [{ name: 'entry', type: 'event' }],
    nodes: [{ id: 'out', kind: 'outputs' }],
    wires: [],
  }),
  palette: (catalog) => catalog.filter((kind) => kind.kind !== 'outputs'),
  canRemove: (node) => node.kind !== 'outputs',
};
