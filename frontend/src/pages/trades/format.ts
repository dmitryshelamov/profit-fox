/** The words of the numbers of «Сделки», «Риск» and the entry dialog (change take-idea-trades). */

type Value = number | string | null | undefined;

const none = (value: Value): value is null | undefined =>
  value === null || value === undefined || Number.isNaN(Number(value));

/** A number with a space between the thousands: `1 000`, `1 250.40`. */
export function grouped(text: string): string {
  const [whole = '0', fraction] = text.split('.');
  return `${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ')}${fraction === undefined ? '' : `.${fraction}`}`;
}

/** An amount of USDT: whole numbers without digits after the point, others with two; `300`, `1 000`, `94.65`. */
export function amountWords(value: Value): string {
  if (none(value)) {
    return '—';
  }

  const amount = Number(value);
  return grouped(Number.isInteger(amount) ? String(amount) : amount.toFixed(2));
}

/** A loss as a negative number of two digits: `−4.20`. */
export function lossWords(value: Value): string {
  if (none(value)) {
    return '—';
  }

  const amount = Math.abs(Number(value));
  return amount === 0 ? '0.00' : `−${amount.toFixed(2)}`;
}

/** The plural form of a count: one, few, many. */
export function plural(count: number, forms: readonly [string, string, string]): string {
  const last = count % 10;
  const tens = count % 100;
  if (last === 1 && tens !== 11) {
    return forms[0];
  }

  return last >= 2 && last <= 4 && (tens < 12 || tens > 14) ? forms[1] : forms[2];
}

/** A signed number of USDT with two digits: `+1.18`. */
export function signedUsdt(value: Value): string {
  if (none(value)) {
    return '—';
  }

  const amount = Number(value);
  return `${amount >= 0 ? '+' : ''}${amount.toFixed(2)}`;
}

/** A percent with its sign and `digits` digits: `+1.2 %`. */
export function signedPercent(percent: Value, digits = 1): string {
  if (none(percent)) {
    return '—';
  }

  const number = Number(percent);
  return `${number >= 0 ? '+' : ''}${number.toFixed(digits)} %`;
}

/** A percent without trailing zeros: `3`, `2.5`. */
export function plainPercent(percent: number): string {
  return String(Number(percent.toFixed(1)));
}
