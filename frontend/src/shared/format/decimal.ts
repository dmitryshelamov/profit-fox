/** A decimal string as whole units of 10^-scale; the digits never become a float. */
function parts(value: string): { units: bigint; scale: number } {
  const [integer = '', fraction = ''] = value.replace(/^[+]/, '').split('.');
  const negative = integer.startsWith('-');
  const units = BigInt(`${integer.replace('-', '')}${fraction}` || '0');

  return { units: negative ? -units : units, scale: fraction.length };
}

/** The text of whole units of 10^-scale, without trailing zeros of the fraction. */
function text(units: bigint, scale: number): string {
  const negative = units < 0n;
  const digits = (negative ? -units : units).toString().padStart(scale + 1, '0');
  const whole = digits.slice(0, digits.length - scale);
  const fraction = digits.slice(digits.length - scale).replace(/0+$/, '');
  const result = fraction === '' ? whole : `${whole}.${fraction}`;

  return negative ? `-${result}` : result;
}

/** Compares two decimal strings exactly: -1 when `left` is less, 1 when it is greater, 0 when they are equal. */
export function compareDecimals(left: string, right: string): -1 | 0 | 1 {
  const a = parts(left);
  const b = parts(right);
  const scale = Math.max(a.scale, b.scale);
  const x = a.units * 10n ** BigInt(scale - a.scale);
  const y = b.units * 10n ** BigInt(scale - b.scale);

  return x < y ? -1 : x > y ? 1 : 0;
}

/** The exact product of two decimal strings. */
export function multiplyDecimals(left: string, right: string): string {
  const a = parts(left);
  const b = parts(right);

  return text(a.units * b.units, a.scale + b.scale);
}

/** Moves the decimal point: 2 places turns a fraction into percent, -2 turns percent back. */
export function shiftDecimal(value: string, places: number): string {
  const { units, scale } = parts(value);

  return places >= 0
    ? text(units * 10n ** BigInt(Math.max(0, places - scale)), Math.max(0, scale - places))
    : text(units, scale - places);
}

/** The exact difference of two decimal strings. */
export function subtractDecimals(left: string, right: string): string {
  const a = parts(left);
  const b = parts(right);
  const scale = Math.max(a.scale, b.scale);

  return text(
    a.units * 10n ** BigInt(scale - a.scale) - b.units * 10n ** BigInt(scale - b.scale),
    scale,
  );
}

/** The quotient of two decimal strings rounded half away from zero to `digits` decimals. */
export function divideDecimals(left: string, right: string, digits: number): string {
  const a = parts(left);
  const b = parts(right);
  const numerator = a.units * 10n ** BigInt(digits + b.scale);
  const denominator = b.units * 10n ** BigInt(a.scale);
  const negative = numerator < 0n !== denominator < 0n;
  const n = numerator < 0n ? -numerator : numerator;
  const d = denominator < 0n ? -denominator : denominator;
  const quotient = (2n * n + d) / (2n * d);

  return text(negative ? -quotient : quotient, digits);
}
