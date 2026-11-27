import { useEffect, useRef, useState } from 'react';

import { api, type RequestError } from '@/api/client';

import type { StrategyKind } from '../document';
import type { SignalUse } from '../model/signals';
import type { Graph } from '../model/types';

/** The delay after the last edit before the graph goes to the server's check. */
export const checkDelayMs = 400;

export interface GraphCheck {
  /** The errors of the last check that came back for a current edit; kept while the next edit is being checked. */
  readonly errors: readonly RequestError[];
  /** What the signal nodes of a strategy point at, by the last check that came back. */
  readonly signals: readonly SignalUse[];
  /** The check of the last edit has come back. */
  readonly checked: boolean;
  /** The check could not be made: the API did not answer. */
  readonly failed: boolean;
}

/**
 * The server's check of a graph without history (spec signal-constructor «Ошибки на холсте»): 0.4 s after the last
 * edit, the request of an older edit is aborted, and an answer that comes back for an older edit is never shown. A
 * graph given as <paramref name="urgent" /> — a signal just added or updated — goes at once, so the node gets its pins.
 * The graph of a strategy goes with its kind and is checked by the rules of that kind.
 */
export function useGraphCheck(
  graph: Graph,
  path: '/api/signals/validate' | '/api/strategies/validate' = '/api/signals/validate',
  urgent: Graph | null = null,
  kind?: StrategyKind,
): GraphCheck {
  const [state, setState] = useState<{
    graph: Graph | null;
    errors: readonly RequestError[];
    signals: readonly SignalUse[];
    failed: boolean;
  }>({
    graph: null,
    errors: [],
    signals: [],
    failed: false,
  });
  const latest = useRef(graph);
  latest.current = graph;

  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(
      async () => {
        try {
          const answer = await api.POST(path, {
            body: { graph: graph as never, ...(kind === undefined ? {} : { kind }) },
            signal: controller.signal,
          });
          if (latest.current !== graph) {
            return;
          }

          const data = answer.data as
            { errors?: RequestError[]; signals?: SignalUse[] } | undefined;
          setState({
            graph,
            errors: data?.errors ?? [],
            signals: data?.signals ?? [],
            failed: data === undefined,
          });
        } catch (problem) {
          if (!controller.signal.aborted && latest.current === graph) {
            setState((current) => ({ ...current, graph, errors: [], failed: true }));
            void problem;
          }
        }
      },
      urgent === graph ? 0 : checkDelayMs,
    );

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [graph, path, urgent, kind]);

  const current = state.graph === graph;
  return {
    errors: state.errors,
    signals: state.signals,
    checked: current && !state.failed,
    failed: current && state.failed,
  };
}
