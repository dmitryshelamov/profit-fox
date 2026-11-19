import { createContext, useContext } from 'react';

import type { Issue } from '../model/issues';
import type { Graph, GraphValue, NodeKind } from '../model/types';

/** What a node on the canvas needs from the editor: the graph, the catalog, the errors and the edits it can make. */
export interface GraphEnvironment {
  readonly graph: Graph;
  readonly catalog: readonly NodeKind[];
  readonly issues: readonly Issue[];
  readonly readOnly: boolean;
  /** Pin references `node.pin` with a wire. */
  readonly connected: ReadonlySet<string>;
  readonly setValue: (node: string, pin: string, value: GraphValue | undefined) => void;
}

export const GraphEnvironmentContext = createContext<GraphEnvironment | null>(null);

export function useGraphEnvironment(): GraphEnvironment {
  const environment = useContext(GraphEnvironmentContext);
  if (environment === null) {
    throw new Error('A node of the graph is drawn outside the editor.');
  }

  return environment;
}
