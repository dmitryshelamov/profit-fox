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

/** A moment of the API as «21.11 00:01», day and time in UTC: the short form of a table. */
export function shortUtc(moment: string): string {
  const iso = new Date(moment).toISOString();
  return `${iso.slice(8, 10)}.${iso.slice(5, 7)} ${iso.slice(11, 16)}`;
}

/** Where a run is: waiting, or going at a Worker with the pairs it has passed. */
export function runProgress(run: Run): string {
  if (run.state === 'queued') {
    return 'ждёт';
  }

  const pairs = `${run.pairsDone} из ${run.pairsTotal ?? '?'} пар`;
  return `${run.worker ?? '?'}: ${pairs}${run.cancelRequested ? ', отменяется' : ''}`;
}

/** A whole number with its thousands apart, «3 366»; a dash for none. */
export function grouped(value: number | string | null | undefined): string {
  return value === null || value === undefined || Number.isNaN(Number(value))
    ? '—'
    : String(Math.round(Number(value))).replace(/\B(?=(\d{3})+(?!\d))/g, '\u00A0');
}

/** A length of time in seconds as «13 мин 7 с», «4 мин 12 с», «2 ч 5 мин» or «42 с». */
export function span(seconds: number): string {
  const whole = Math.max(0, Math.round(seconds));
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  return hours > 0
    ? `${hours} ч ${minutes} мин`
    : minutes > 0
      ? `${minutes} мин ${whole % 60} с`
      : `${whole} с`;
}

/** The time of day of a moment of the API in UTC, «00:14». */
export function clockUtc(moment: string): string {
  return new Date(moment).toISOString().slice(11, 16);
}

/** A long id or hash by its first and last 8 signs, `8c33b686…c4bab201`; a short one whole. */
export function shortened(text: string): string {
  return text.length > 17 ? `${text.slice(0, 8)}…${text.slice(-8)}` : text;
}
