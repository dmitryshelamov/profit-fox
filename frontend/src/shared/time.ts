/**
 * The moments of the bench as the screens write them: in the zone of the browser (spec bench-ui «Местное время»), each
 * moment with its own offset, so summer time is kept. The API and the engine count in UTC; only the text is local. Days
 * of UTC — the ends of history of a run, the end of exploration — are days, not moments, and are not written here.
 */

const two = (value: number) => String(value).padStart(2, '0');

/** A moment in milliseconds as «26.11 14:00»: the day, the month and the time. */
export function localMoment(ms: number): string {
  const at = new Date(ms);
  return `${two(at.getDate())}.${two(at.getMonth() + 1)} ${localClock(ms)}`;
}

/** A moment in milliseconds as «2026-11-26 14:00». */
export function localDateTime(ms: number): string {
  const at = new Date(ms);
  return `${at.getFullYear()}-${two(at.getMonth() + 1)}-${two(at.getDate())} ${localClock(ms)}`;
}

/** The time of day of a moment in milliseconds, «14:00». */
export function localClock(ms: number): string {
  const at = new Date(ms);
  return `${two(at.getHours())}:${two(at.getMinutes())}`;
}

/** The time of day of a moment in milliseconds with its seconds, «14:01:01». */
export function localClockSeconds(ms: number): string {
  const at = new Date(ms);
  return `${localClock(ms)}:${two(at.getSeconds())}`;
}

/**
 * The zone of the browser by its offset from UTC at `now`: «UTC+7», «UTC+5:30», «UTC-3», «UTC+0». The columns of moments
 * carry it in their titles.
 */
export function zoneLabel(now: Pick<Date, 'getTimezoneOffset'> = new Date()): string {
  const east = -now.getTimezoneOffset();
  const size = Math.abs(east);
  const minutes = size % 60;
  return `UTC${east < 0 ? '-' : '+'}${Math.floor(size / 60)}${minutes === 0 ? '' : `:${two(minutes)}`}`;
}
