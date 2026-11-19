import type { Graph, NodeKind } from '@/modules/graph-editor/model/types';

import h0001File from '../../../bench/hypotheses/H0001-funding-short-4h/hypothesis.json';
import catalogFile from './catalog.fixture.json';

/** The node catalog as the bench API serves it (`GET /api/nodes`). */
export const catalog = catalogFile as NodeKind[];

/** The file of H0001 from the repository. */
export const h0001 = h0001File;

/** The signal graph of H0001 from the repository, a fresh copy each time. */
export function h0001Graph(): Graph {
  return structuredClone(h0001File.signal) as Graph;
}
