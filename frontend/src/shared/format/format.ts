const NBSP = ' ';

/** Groups the integer digits of a decimal string by thousands; the digits are never turned into a number. */
export function groupDigits(value: string): string {
  const [integer = '', fraction] = value.split('.');
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, NBSP);

  return fraction === undefined ? grouped : `${grouped}.${fraction}`;
}

const two = (value: number) => String(value).padStart(2, '0');

/** `02.11 14:05`: a moment in UTC. */
export function formatMoment(iso: string): string {
  const date = new Date(iso);

  return `${two(date.getUTCDate())}.${two(date.getUTCMonth() + 1)} ${two(date.getUTCHours())}:${two(date.getUTCMinutes())}`;
}

/** `01.01.2024`: a day in UTC. */
export function formatDay(iso: string): string {
  const date = new Date(iso);

  return `${two(date.getUTCDate())}.${two(date.getUTCMonth() + 1)}.${date.getUTCFullYear()}`;
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

/** `2024-02-01 00:00`: a moment in UTC with its year. */
export function formatUtc(iso: string): string {
  return new Date(iso).toISOString().slice(0, 16).replace('T', ' ');
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

/** A decimal string to a tenth, without a zero tenth: `12.00` is `12`, `16.666…` is `16.7`. */
export function roundTenth(value: string): string {
  return roundDecimal(value, 1).replace(/\.0$/, '');
}

/** A decimal string for the screen: rounded, grouped by thousands, with `+` in front of a positive one when asked. */
export function showDecimal(value: string, digits: number, withSign = false): string {
  const rounded = roundDecimal(value, digits);

  return groupDigits(withSign ? signed(rounded) : rounded);
}
