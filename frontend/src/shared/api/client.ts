import createClient from 'openapi-fetch';

import type { components, paths } from './generated/schema';

export type Schemas = components['schemas'];
export type Usage = Schemas['UsageResponse'];
export type AffectedBot = Schemas['AffectedBotResponse'];

/**
 * The only HTTP client. Requests go to the page's own origin: Vite in development and Caddy in production forward
 * `/api` to the API. `fetch` is resolved on every call, so a test can replace the global.
 */
export const api = createClient<paths>({
  baseUrl: globalThis.location?.origin ?? '',
  fetch: (request) => globalThis.fetch(request),
});

/** A failed answer of the API: the Problem Details the backend sends, with its validation errors and conflicts. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly errors: Readonly<Record<string, readonly string[]>> = {},
    readonly usages: readonly Usage[] = [],
    readonly bots: readonly AffectedBot[] = [],
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

interface Problem {
  readonly title?: string;
  readonly detail?: string;
  readonly errors?: Record<string, string[]>;
  readonly usages?: Usage[];
  readonly bots?: AffectedBot[];
}

/** Returns the data of a successful answer or throws {@link ApiError}. */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.response.ok) {
    return result.data as T;
  }

  const problem = (result.error ?? {}) as Problem;

  throw new ApiError(
    result.response.status,
    problem.detail ?? problem.title ?? result.response.statusText,
    problem.errors,
    problem.usages,
    problem.bots,
  );
}
