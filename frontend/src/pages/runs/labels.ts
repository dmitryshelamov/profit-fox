import type { Run } from '@/api/runs';

/** How often a page asks again while a run waits or goes. */
export const pollMs = 3000;

/** The states of a run in the words of the bench (spec bench-ui «Цвет состояния прогона»). */
const states: Record<string, string> = {
  queued: 'В очереди',
  running: 'Идёт',
  done: 'Готов',
  failed: 'Упал',
  cancelled: 'Отменён',
};

export const stateLabel = (state: string) => states[state] ?? state;

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

  return `${run.worker ?? '?'}: ${run.pairsDone} из ${run.pairsTotal ?? '?'} пар`;
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

/** The history of a run, «2023-01-01 — 2026-06-01»: its start and its end, which is not in it. */
export function historyWords(run: Run): string {
  return `${run.settings.start} — ${run.settings.end}`;
}

/** The pairs of a run: all of them, or the list; a long list by its first pairs and their count. */
export function pairsWords(run: Run): string {
  const pairs = run.settings.pairs;
  if (pairs === null || pairs === undefined) {
    return 'все пары';
  }

  return pairs.length > 3
    ? `${pairs.slice(0, 3).join(', ')} и ещё ${pairs.length - 3}`
    : pairs.join(', ');
}

/** A share as a percent with two digits and its sign, `+0.12 %`; a dash for none. */
export function signedPercent(value: number | string | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(Number(value))) {
    return '—';
  }

  const percent = Number(value) * 100;
  return `${percent >= 0 ? '+' : ''}${percent.toFixed(2)} %`;
}

/** An amount of USDT with two digits and its sign, `-1.54`; a dash for none. */
export function signedUsdt(value: number | string | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(Number(value))) {
    return '—';
  }

  const amount = Number(value);
  return `${amount >= 0 ? '+' : ''}${amount.toFixed(2)}`;
}
