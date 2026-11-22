const months = ['янв', 'фев', 'мар', 'апр', 'май', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];

/** The mark of a month on the time scale: three Russian letters, by UTC like every moment of the bench. */
export function monthTick(seconds: number): string {
  return months[new Date(seconds * 1000).getUTCMonth()] ?? '';
}

const whole = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0, useGrouping: 'always' });
const cents = new Intl.NumberFormat('ru-RU', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const small = new Intl.NumberFormat('ru-RU', { maximumSignificantDigits: 5 });

/**
 * A price on the scale of the chart with the digits its size needs: thousands in groups without a fraction, hundreds
 * with two digits after the comma, and five significant digits for a price under a hundred, so a coin of a fraction
 * of a cent is not shown as zero.
 */
export function priceText(price: number): string {
  const size = Math.abs(price);
  return (size >= 1000 ? whole : size >= 100 ? cents : small).format(price);
}
