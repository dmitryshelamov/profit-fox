const NBSP = ' ';

/** Groups the integer digits of a decimal string by thousands; the digits are never turned into a number. */
export function groupDigits(value: string): string {
  const [integer = '', fraction] = value.split('.');
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, NBSP);

  return fraction === undefined ? grouped : `${grouped}.${fraction}`;
}

const two = (value: number) => String(value).padStart(2, '0');

/** `02.11 14:05`: a moment in the local zone of the browser. */
export function formatMoment(iso: string): string {
  const date = new Date(iso);

  return `${two(date.getDate())}.${two(date.getMonth() + 1)} ${two(date.getHours())}:${two(date.getMinutes())}`;
}

/** `01.01.2024`: a day in the local zone of the browser. */
export function formatDay(iso: string): string {
  const date = new Date(iso);

  return `${two(date.getDate())}.${two(date.getMonth() + 1)}.${date.getFullYear()}`;
}

/** `14:32`: a time of day in the local zone of the browser. */
export function formatClock(iso: string): string {
  const date = new Date(iso);

  return `${two(date.getHours())}:${two(date.getMinutes())}`;
}

/** The start of the UTC day of `now`: the server counts «today» from it. */
export function utcDayStart(now: number): string {
  const date = new Date(now);

  return new Date(
    Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()),
  ).toISOString();
}

/** The form of "комбинация" that goes with the count. */
export function combinationsWord(count: number | string): string {
  const rest = Number(BigInt(count) % 100n);

  if (rest >= 11 && rest <= 14) {
    return 'комбинаций';
  }

  switch (rest % 10) {
    case 1:
      return 'комбинация';
    case 2:
    case 3:
    case 4:
      return 'комбинации';
    default:
      return 'комбинаций';
  }
}

/** `2024-02-01 00:00`: a moment in the local zone of the browser with its year. */
export function formatFullMoment(iso: string): string {
  const date = new Date(iso);

  return `${date.getFullYear()}-${two(date.getMonth() + 1)}-${two(date.getDate())} ${two(date.getHours())}:${two(date.getMinutes())}`;
}

/** A decimal string with `+` in front when it is above zero; the digits stay as they are. */
export function signed(value: string): string {
  return /^-|^0*\.?0*$/.test(value) ? value : `+${value}`;
}

/** Rounds a decimal string half away from zero to `digits` decimals; the digits are never turned into a number. */
export function roundDecimal(value: string, digits: number): string {
  const negative = value.startsWith('-');
  const [integer = '', fraction = ''] = value.replace(/^-/, '').split('.');
  const kept = integer + fraction.slice(0, digits).padEnd(digits, '0');
  const up = (fraction[digits] ?? '0') >= '5';
  const rounded = (BigInt(kept) + (up ? 1n : 0n)).toString().padStart(digits + 1, '0');
  const whole = rounded.slice(0, rounded.length - digits);
  const result = digits === 0 ? whole : `${whole}.${rounded.slice(-digits)}`;

  return negative && /[1-9]/.test(result) ? `-${result}` : result;
}

/** A decimal string for the screen: rounded, grouped by thousands, with `+` in front of a positive one when asked. */
export function showDecimal(value: string, digits: number, withSign = false): string {
  const rounded = roundDecimal(value, digits);

  return groupDigits(withSign ? signed(rounded) : rounded);
}
