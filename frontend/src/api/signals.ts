import { api, type RequestError, type Schemas, unwrap } from './client';

export type SignalListItem = Schemas['SignalListItem'];
export type SignalResponse = Schemas['SignalResponse'];
export type NodeKindResponse = Schemas['NodeKindResponse'];

/** The answer to a write of a signal: done, or refused with the errors the page shows. */
export type Written<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly errors: readonly RequestError[] };

/**
 * A write that the API may refuse with errors (`400`, `409`, `422`): the errors come back as a value, any other failure
 * throws as {@link unwrap} does.
 */
export async function written<T>(result: {
  data?: T;
  error?: unknown;
  response: Response;
}): Promise<Written<T>> {
  const status = result.response.status;
  if (
    (status === 400 || status === 409 || status === 422) &&
    result.error &&
    typeof result.error === 'object'
  ) {
    const body = result.error as { errors?: unknown; detail?: string; title?: string };
    if (Array.isArray(body.errors)) {
      return { ok: false, errors: body.errors as RequestError[] };
    }

    // Another body — such as the validation problem of ASP.NET, whose errors are an object — is one general refusal, not a
    // list to filter (review R2).
    if ('errors' in body || body.detail !== undefined || body.title !== undefined) {
      return {
        ok: false,
        errors: [
          { code: 'Refused', reason: body.detail ?? body.title ?? result.response.statusText },
        ],
      };
    }
  }

  return { ok: true, value: unwrap(result) };
}

export async function listSignals(): Promise<SignalListItem[]> {
  return unwrap(await api.GET('/api/signals'));
}

export async function listNodeKinds(): Promise<NodeKindResponse[]> {
  return unwrap(await api.GET('/api/nodes'));
}
