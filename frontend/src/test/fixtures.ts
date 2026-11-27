import type { Graph, NodeKind } from '@/modules/graph-editor/model/types';

import catalogFile from './catalog.fixture.json';
import h0001SignalFile from './h0001Signal.fixture.json';

/** The node catalog as the bench API serves it (`GET /api/nodes`). */
export const catalog = catalogFile as NodeKind[];

/** The signal graph of H0001 (`funding-high` of the reference strategy), a fresh copy each time. */
export function h0001Graph(): Graph {
  return structuredClone(h0001SignalFile) as Graph;
}
