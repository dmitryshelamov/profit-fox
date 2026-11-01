import { createContext, useContext } from 'react';

import type { Issue } from '../model/issues';
import type { PinContext } from '../model/pins';
import type { CatalogNode, GraphNodeDef, NodeValue } from '../model/types';

/** What every node of the canvas needs besides its own definition. */
export interface GraphEnvironment {
  readonly nodes: readonly GraphNodeDef[];
  readonly catalog: readonly CatalogNode[];
  readonly context: PinContext;
  readonly issues: readonly Issue[];
  readonly connected: ReadonlySet<string>;
  readonly readOnly: boolean;
  readonly setValue: (nodeId: string, pin: string, value: NodeValue | undefined) => void;
}

export const GraphEnvironmentContext = createContext<GraphEnvironment | undefined>(undefined);

export function useGraphEnvironment(): GraphEnvironment {
  const environment = useContext(GraphEnvironmentContext);

  if (!environment) {
    throw new Error('Graph nodes render only inside GraphCanvas.');
  }

  return environment;
}
