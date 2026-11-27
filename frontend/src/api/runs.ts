import { api, ApiError, type RequestError, type Schemas, unwrap } from './client';

/** A backtest of a strategy: «прогон» in the words of the bench. */
export type Run = Schemas['BacktestResponse'];
export type RunSet = Schemas['BacktestSetResponse'];
export type SetNumbers = Schemas['SetNumbersResponse'];
export type SetPairs = Schemas['BacktestSetPairsResponse'];
export type RunRequest = Schemas['CreateBacktestRequest'];

/** The answer to a queueing: the run, or the errors of its settings. */
export type Enqueued =
  | { readonly ok: true; readonly run: Run }
  | { readonly ok: false; readonly errors: readonly RequestError[] };

/** The errors of a refusal with a body of errors, or none. */
function refusal(result: { error?: unknown; response: Response }): RequestError[] | null {
  return result.error && typeof result.error === 'object' && 'errors' in result.error
    ? (result.error as Schemas['ErrorResponse']).errors
    : null;
}

/** Puts a run in the queue (spec bench-run-queue «Постановка прогона»). */
export async function enqueueRun(request: RunRequest): Promise<Enqueued> {
  const result = await api.POST('/api/backtests', { body: request });
  const errors = refusal(result);
  if (errors !== null) {
    return { ok: false, errors };
  }

  if (result.response.status === 404) {
    const problem = (result.error ?? {}) as { detail?: string };
    return {
      ok: false,
      errors: [{ code: 'NotFound', reason: problem.detail ?? 'Стратегии нет.' }],
    };
  }

  return { ok: true, run: unwrap(result) };
}

/** Puts the set <paramref name="number" /> of a done run in the queue on the rest of history. */
export async function enqueueRest(id: string, number: number): Promise<Enqueued> {
  const result = await api.POST('/api/backtests/{id}/sets/{number}/rest', {
    params: { path: { id, number } },
  });
  const errors = refusal(result);
  return errors === null ? { ok: true, run: unwrap(result) } : { ok: false, errors };
}

/** The runs that wait and go, in the order of the queue. */
export async function listActiveRuns(): Promise<Run[]> {
  return unwrap(await api.GET('/api/backtests', { params: { query: { active: true } } }));
}

/** The latest finished runs, the newest first. */
export async function listFinishedRuns(limit = 50): Promise<Run[]> {
  return unwrap(await api.GET('/api/backtests', { params: { query: { active: false, limit } } }));
}

export async function getRun(id: string): Promise<Run> {
  const result = await api.GET('/api/backtests/{id}', { params: { path: { id } } });
  if (result.response.status === 404) {
    throw new ApiError(404, `Прогона ${id} нет.`);
  }

  return unwrap(result);
}

/** One set of a run with the numbers of its pairs. */
export async function getRunSet(id: string, number: number): Promise<SetPairs> {
  const result = await api.GET('/api/backtests/{id}/sets/{number}', {
    params: { path: { id, number } },
  });
  if (result.response.status === 404) {
    throw new ApiError(404, `У прогона ${id} нет набора ${number}.`);
  }

  return unwrap(result);
}

/** Cancels a run; a run that has ended meanwhile answers 409 and that is no failure for the page. */
export async function cancelRun(id: string): Promise<void> {
  const result = await api.POST('/api/backtests/{id}/cancel', { params: { path: { id } } });
  if (result.response.status !== 409) {
    unwrap(result);
  }
}
