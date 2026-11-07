import { TickMarkType } from 'lightweight-charts';

import { formatClock, formatFullMoment } from '@/shared/format/format';

const months = ['янв', 'фев', 'мар', 'апр', 'май', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];

const isoOf = (seconds: number) => new Date(seconds * 1000).toISOString();

/**
 * The label of a mark of the time axis in the local zone: a year, a month, a day of the month or the time of day. The
 * chart keeps UTC seconds and places the marks on UTC boundaries; only the label is local.
 */
export function tickMarkText(seconds: number, type: TickMarkType): string {
  const date = new Date(seconds * 1000);

  switch (type) {
    case TickMarkType.Year:
      return String(date.getFullYear());
    case TickMarkType.Month:
      return months[date.getMonth()]!;
    case TickMarkType.DayOfMonth:
      return String(date.getDate());
    default:
      return formatClock(isoOf(seconds));
  }
}

/** The moment under the crosshair of a chart in the local zone: `2024-01-05 17:00`. */
export const crosshairTime = (seconds: number) => formatFullMoment(isoOf(seconds));
