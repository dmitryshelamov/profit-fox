import type { Run } from '@/api/runs';

/** How often a page asks again while a run waits or goes. */
export const pollMs = 3000;

const periods: Record<string, string> = {
  exploration: 'разведка',
  holdout: 'отложенный',
  'new-data': 'новые данные',
};

const kinds: Record<string, string> = { verdict: 'с вердиктом', parity: 'сверка' };

const statuses: Record<string, string> = {
  passed: 'Прошла',
  rejected: 'Отвергнута',
  insufficient_data: 'Данных мало',
  parity: 'Сверка',
  failed: 'Упала',
  cancelled: 'Отменён',
  // Where a run of the queue is, for the badge of a run that has no line yet.
  running: 'Идёт',
  queued: 'В очереди',
};

export const periodOrder = ['exploration', 'holdout', 'new-data'] as const;

export const periodLabel = (period: string) => periods[period] ?? period;
export const kindLabel = (kind: string) => kinds[kind] ?? kind;
export const statusLabel = (status: string) => statuses[status] ?? status;

/** A number of the API (a JSON number, or a string for a non-finite one) with a fixed count of digits; a dash for none. */
export function fixed(value: number | string | null | undefined, digits: number): string {
  return value === null || value === undefined || Number.isNaN(Number(value))
    ? '—'
    : Number(value).toFixed(digits);
}

/** A moment of the API as «2026-11-20 08:07 UTC»; the app shows UTC everywhere. */
export function utc(moment: string | null): string {
  return moment === null
    ? '—'
    : `${new Date(moment).toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

/** Where a run is: waiting, or going at a Worker with the pairs it has passed. */
export function runProgress(run: Run): string {
  if (run.state === 'queued') {
    return 'ждёт';
  }

  const pairs = `${run.pairsDone} из ${run.pairsTotal ?? '?'} пар`;
  return `${run.worker ?? '?'}: ${pairs}${run.cancelRequested ? ', отменяется' : ''}`;
}
