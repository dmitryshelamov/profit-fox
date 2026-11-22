import { draftKey, type DraftOwner, strategyDraftKey } from './drafts/drafts';
import type { Graph, GraphNode, NodeKind } from './model/types';

/**
 * The rules of a kind of document the editor edits: what it starts with, what its palette offers, which nodes may go,
 * where its graph is checked and under which key its draft is kept. A signal has exactly one output, the event
 * `entry`, and one `outputs` node that is never removed and is not in the palette. A strategy starts with an `enter` of
 * 100 USDT, the `position` and a `close-after`, wired by the orders and the position, without a side and a time; any
 * node may go, the check names what is missing.
 */
export interface DocumentRules {
  readonly kind: 'signal' | 'strategy';
  /** The graph a new document starts with. */
  readonly start: () => Graph;
  /** The kinds the palette offers; the saved signals of a strategy come apart from them. */
  readonly palette: (catalog: readonly NodeKind[]) => NodeKind[];
  readonly canRemove: (node: GraphNode) => boolean;
  /** Where the graph is checked without history. */
  readonly checkPath: '/api/signals/validate' | '/api/strategies/validate';
  /** The key of the draft of a saved document or of a new one. */
  readonly draftKey: (owner: DraftOwner) => string;
}

/**
 * The wires of the position of a new strategy: the order of the entry into it, the position into the exit, the order of
 * the exit back into it.
 */
export function positionWires(enter: string, exit: string): Graph['wires'] {
  return [
    { from: `${enter}.order`, to: 'position.orders' },
    { from: 'position.position', to: `${exit}.position` },
    { from: `${exit}.order`, to: 'position.orders' },
  ];
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
  checkPath: '/api/signals/validate',
  draftKey,
};

export const strategyRules: DocumentRules = {
  kind: 'strategy',
  start: () => ({
    formatVersion: 1,
    parameters: [],
    outputs: [],
    nodes: [
      { id: 'enter', kind: 'enter', inputs: { size: 100 } },
      { id: 'position', kind: 'position' },
      { id: 'close-after', kind: 'close-after' },
    ],
    wires: positionWires('enter', 'close-after'),
  }),
  palette: (catalog) => catalog.filter((kind) => kind.kind !== 'outputs' && kind.kind !== 'signal'),
  canRemove: () => true,
  checkPath: '/api/strategies/validate',
  draftKey: strategyDraftKey,
};
