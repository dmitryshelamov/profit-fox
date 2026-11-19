import { useEffect, useRef, useState } from 'react';

import { api, type RequestError } from '@/api/client';

import type { Graph } from '../model/types';

/** The delay after the last edit before the graph goes to the server's check. */
export const checkDelayMs = 400;

export interface GraphCheck {
  /** The errors of the last check that came back for a current edit; kept while the next edit is being checked. */
  readonly errors: readonly RequestError[];
  /** The check of the last edit has come back. */
  readonly checked: boolean;
  /** The check could not be made: the API did not answer. */
  readonly failed: boolean;
}

/**
 * The server's check of a graph without history (spec signal-constructor «Ошибки на холсте»): 0.4 s after the last
 * edit, the request of an older edit is aborted, and an answer that comes back for an older edit is never shown.
 */
export function useGraphCheck(graph: Graph): GraphCheck {
  const [state, setState] = useState<{
    graph: Graph | null;
    errors: readonly RequestError[];
    failed: boolean;
  }>({
    graph: null,
    errors: [],
    failed: false,
  });
  const latest = useRef(graph);
  latest.current = graph;

  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(async () => {
      try {
        const answer = await api.POST('/api/signals/validate', {
          body: { graph: graph as never },
          signal: controller.signal,
        });
        if (latest.current !== graph) {
          return;
        }

        setState({ graph, errors: answer.data?.errors ?? [], failed: answer.data === undefined });
      } catch (problem) {
        if (!controller.signal.aborted && latest.current === graph) {
          setState({ graph, errors: [], failed: true });
          void problem;
        }
      }
    }, checkDelayMs);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [graph]);

  const current = state.graph === graph;
  return {
    errors: state.errors,
    checked: current && !state.failed,
    failed: current && state.failed,
  };
}
