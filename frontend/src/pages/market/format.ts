/**
 * The words of the numbers of «Рынок», «Идеи» and the page of a pair (change add-ideas-and-market): the API gives shares,
 * the page shows percents; amounts of USDT go in millions and billions; times are UTC.
 */

type Value = number | string | null | undefined;

const none = (value: Value): value is null | undefined =>
  value === null || value === undefined || Number.isNaN(Number(value));

/** A share as a percent with its sign: `+0.21 %`; a dash for none. */
export function changeText(value: Value, digits = 2): string {
  if (none(value)) {
    return '—';
  }

  const percent = Number(value) * 100;
  return `${percent >= 0 ? '+' : ''}${percent.toFixed(digits)} %`;
}

/** A rate of funding as a percent with four digits: `0.0100 %`. */
export function rateText(value: Value): string {
  return none(value) ? '—' : `${(Number(value) * 100).toFixed(4)} %`;
}

/** An amount of USDT in billions or millions: `12.4 млрд`, `842 млн`. */
export function amountText(value: Value): string {
  if (none(value)) {
    return '—';
  }

  const amount = Number(value);
  const billions = amount / 1e9;
  if (Math.abs(billions) >= 1) {
    return `${billions.toFixed(billions >= 10 ? 1 : 2)} млрд`;
  }

  const millions = amount / 1e6;
  return Math.abs(millions) >= 1
    ? `${Math.round(millions)} млн`
    : `${(amount / 1e3).toFixed(0)} тыс`;
}

/** A price as the exchange writes it, without trailing zeros past its digits. */
export function priceWords(value: Value): string {
  if (none(value)) {
    return '—';
  }

  const price = Number(value);
  const size = Math.abs(price);
  const digits = size >= 1000 ? 1 : size >= 100 ? 2 : size >= 1 ? 4 : 6;
  const [whole = '', fraction] = price.toFixed(digits).split('.');
  return `${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ')}${fraction === undefined ? '' : `.${fraction}`}`;
}

/** A moment in milliseconds as «22.11 14:30», UTC. */
export function momentText(ms: Value): string {
  if (none(ms)) {
    return '—';
  }

  const iso = new Date(Number(ms)).toISOString();
  return `${iso.slice(8, 10)}.${iso.slice(5, 7)} ${iso.slice(11, 16)}`;
}

/** A moment in milliseconds as «14:32:05 UTC». */
export function clockText(ms: Value): string {
  return none(ms) ? '—' : `${new Date(Number(ms)).toISOString().slice(11, 19)} UTC`;
}

/** A length of time in milliseconds as «1 ч 58 мин» or «43 мин». */
export function leftText(ms: number): string {
  const minutes = Math.max(0, Math.floor(ms / 60_000));
  const hours = Math.floor(minutes / 60);
  return hours > 0 ? `${hours} ч ${minutes % 60} мин` : `${minutes} мин`;
}

/** The tone of a signed number: green above zero, red below, plain at zero or none. */
export function signTone(value: Value): string {
  return none(value) || Number(value) === 0
    ? 'text-text'
    : Number(value) > 0
      ? 'text-ok'
      : 'text-err';
}
