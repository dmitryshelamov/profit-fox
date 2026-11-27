import createClient from 'openapi-fetch';

import type { components, paths } from './generated/schema';

export type Schemas = components['schemas'];
export type Instrument = Schemas['InstrumentResponse'];
export type Preview = Schemas['PreviewResponse'];
export type PreviewCandle = Schemas['PreviewCandle'];
export type PreviewEvent = Schemas['PreviewEvent'];
export type StrategyPreview = Schemas['StrategyPreviewResponse'];
export type PreviewOrder = Schemas['PreviewOrder'];
export type PreviewCycle = Schemas['PreviewCycle'];
export type PreviewFill = Schemas['PreviewFill'];
export type RequestError = Schemas['RequestError'];

/**
 * The client of the bench API. Requests go to the page's own origin: Vite forwards `/api` to the API. `fetch` is
 * resolved on every call, so a test can replace the global.
 */
export const api = createClient<paths>({
  baseUrl: globalThis.location?.origin ?? '',
  fetch: (request) => globalThis.fetch(request),
});

/** The answer of the API that is neither a preview nor a refusal with errors: the service is down or the body is wrong. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/** Returns the data of a successful answer or throws {@link ApiError}. */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.response.ok) {
    return result.data as T;
  }

  const problem = (result.error ?? {}) as { title?: string; detail?: string };
  throw new ApiError(
    result.response.status,
    problem.detail ?? problem.title ?? result.response.statusText,
  );
}
