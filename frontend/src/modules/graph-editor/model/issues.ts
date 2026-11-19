import type { RequestError } from '@/api/client';
import { placeOf } from '@/preview/preview';

/** An error of the check where the canvas shows it: on a node, on its pin, on a wire, or on the graph as a whole. */
export interface Issue {
  readonly node?: string;
  readonly pin?: string;
  readonly wire?: { readonly from: string; readonly to: string };
  readonly parameter?: string;
  readonly place: string;
  readonly code: string;
  readonly reason: string;
}

/**
 * The errors of the server's check by place. An error of a wire also marks the input it goes to; the nodes of a cycle
 * each get the error.
 */
export function issuesOf(errors: readonly RequestError[]): Issue[] {
  return errors.flatMap((error): Issue[] => {
    const base = { place: placeOf(error), code: error.code, reason: error.reason };
    if (error.wire) {
      const dot = error.wire.to.indexOf('.');
      return [
        {
          ...base,
          wire: error.wire,
          node: dot === -1 ? undefined : error.wire.to.slice(0, dot),
          pin: dot === -1 ? undefined : error.wire.to.slice(dot + 1),
        },
      ];
    }

    if (error.nodes && error.nodes.length > 0) {
      return error.nodes.map((node) => ({ ...base, node }));
    }

    return [
      {
        ...base,
        node: error.node ?? undefined,
        pin: error.pin ?? undefined,
        parameter: error.parameter ?? undefined,
      },
    ];
  });
}

/** The number of distinct errors, as the button «Ошибки» counts them: a cycle is one error. */
export function errorCount(errors: readonly RequestError[]): number {
  return errors.length;
}
