import { TickMarkType, type Time } from 'lightweight-charts';

import { localClock, localClockSeconds, localDateTime } from '@/shared/time';

const months = ['янв', 'фев', 'мар', 'апр', 'май', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];

/** The mark of a month on the time scale: three Russian letters, by the month of the zone of the browser. */
export function monthTick(seconds: number): string {
  return months[new Date(seconds * 1000).getMonth()] ?? '';
}

/**
 * A mark of the time scale in the zone of the browser (spec bench-ui «Местное время», design decision 4): the year, the
 * month, the day of the month, the time. The data of the chart stay in seconds of UTC; only the text is local. The
 * library puts the marks of days at midnight of UTC, so east of UTC the mark of a day stands at a later hour of the same
 * local day.
 */
export function timeTick(time: Time, type: TickMarkType): string {
  const seconds = Number(time);
  const at = new Date(seconds * 1000);
  switch (type) {
    case TickMarkType.Year:
      return String(at.getFullYear());
    case TickMarkType.Month:
      return monthTick(seconds);
    case TickMarkType.DayOfMonth:
      return String(at.getDate());
    case TickMarkType.TimeWithSeconds:
      return localClockSeconds(seconds * 1000);
    default:
      return localClock(seconds * 1000);
  }
}

/** The time of the crosshair in the zone of the browser, `2026-11-26 14:00`. */
export function crosshairTime(time: Time): string {
  return localDateTime(Number(time) * 1000);
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
