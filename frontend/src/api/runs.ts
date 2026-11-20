import { api, ApiError, type Schemas, unwrap } from './client';

export type Run = Schemas['RunResponse'];
export type RunResult = Schemas['RunResultResponse'];
export type RunAvailability = Schemas['RunAvailabilityResponse'];

/** The answer to a queueing: the run, or the refusal of the ledger rules with the run that stands in the way. */
export type Enqueued =
  | { readonly ok: true; readonly run: Run }
  | { readonly ok: false; readonly reason: string; readonly runId: string | null };

export async function enqueueRun(request: {
  readonly hypothesis: string;
  readonly version: number;
  readonly period: string;
  readonly kind: string;
}): Promise<Enqueued> {
  const result = await api.POST('/api/runs', { body: request });
  if (result.response.status === 409) {
    const conflict = result.error as Schemas['RunConflictResponse'];
    return { ok: false, reason: conflict.reason, runId: conflict.runId };
  }

  if (
    (result.response.status === 400 || result.response.status === 422) &&
    result.error &&
    typeof result.error === 'object' &&
    'errors' in result.error
  ) {
    const errors = (result.error as Schemas['ErrorResponse']).errors;
    return { ok: false, reason: errors.map((error) => error.reason).join('; '), runId: null };
  }

  return { ok: true, run: unwrap(result) };
}

/** The runs that wait and go, in the order of the queue. */
export async function listActiveRuns(): Promise<Run[]> {
  return unwrap(await api.GET('/api/runs', { params: { query: { active: true } } }));
}

/** The latest finished runs, the newest first. */
export async function listFinishedRuns(limit = 50): Promise<Run[]> {
  return unwrap(await api.GET('/api/runs', { params: { query: { active: false, limit } } }));
}

export async function getRun(id: string): Promise<Run> {
  const result = await api.GET('/api/runs/{id}', { params: { path: { id } } });
  if (result.response.status === 404) {
    throw new ApiError(404, `Прогона ${id} нет.`);
  }

  return unwrap(result);
}

/** Cancels a run; a run that has ended meanwhile answers 409 and that is no failure for the page. */
export async function cancelRun(id: string): Promise<void> {
  const result = await api.POST('/api/runs/{id}/cancel', { params: { path: { id } } });
  if (result.response.status !== 409) {
    unwrap(result);
  }
}
