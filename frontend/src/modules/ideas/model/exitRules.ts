import { isDecimalText } from '@/shared/api/jsonNumber';
import { compareDecimals, shiftDecimal, subtractDecimals } from '@/shared/format/decimal';
import { roundDecimal } from '@/shared/format/format';

import { candlesText } from './labels';

/** The candles of a time on the grid of an interval, and what the rounding did, when it did anything. */
export interface CandlesOfTime {
  readonly candles: number;
  readonly note: string | null;
}

/**
 * A time in hours as candles of the interval: up to the first value of the grid at or above it, the last one, 7 days, for a longer
 * time. None for text that is no positive number.
 */
export function candlesOfHours(
  hours: string,
  interval: string,
  grid: readonly number[],
): CandlesOfTime | null {
  if (!isDecimalText(hours) || Number(hours) <= 0) {
    return null;
  }

  const exact = (Number(hours) * 60) / Number(interval);
  const candles = grid.find((step) => step >= exact) ?? grid[grid.length - 1]!;

  return {
    candles,
    note: candles === exact ? null : `введено ${hours} ч — округлено до ${candlesText(candles)}`,
  };
}

const oneDecimal = (value: number) => String(Math.round(value * 10) / 10);

/** How long candles of an interval last: minutes under an hour, hours under a day, days, with at most one decimal. */
export function durationText(candles: number, interval: string): string {
  const minutes = candles * Number(interval);

  if (minutes < 60) {
    return `${minutes} мин`;
  }

  return minutes < 1440 ? `${oneDecimal(minutes / 60)} ч` : `${oneDecimal(minutes / 1440)} дн`;
}

interface Expectancies {
  readonly current: { readonly expectancy: string } | null;
  readonly candidate: { readonly expectancy: string } | null;
}

/**
 * The warning of a fit to the past: the candidate is better by the expectancy per entry over 90 days, while over the last 30 it adds
 * less than half of that.
 */
export function overfitWarning(last90: Expectancies, last30: Expectancies): string | null {
  if (!last90.current || !last90.candidate || !last30.current || !last30.candidate) {
    return null;
  }

  const gain90 = subtractDecimals(last90.candidate.expectancy, last90.current.expectancy);
  const gain30 = subtractDecimals(last30.candidate.expectancy, last30.current.expectancy);

  // Less than half: the 30 days add less than the 90 days add beyond them.
  return compareDecimals(gain90, '0') > 0 &&
    compareDecimals(subtractDecimals(gain90, gain30), gain30) > 0
    ? 'на последних 30 днях почти не лучше — похоже на подгонку'
    : null;
}

interface RuleValues {
  readonly life: number;
  readonly target: string | null;
  readonly window: number | null;
}

/** Whether two rules have the same lifetime, target and window. */
export function sameRule(left: RuleValues, right: RuleValues): boolean {
  return (
    left.life === right.life &&
    left.window === right.window &&
    (left.target === null || right.target === null
      ? left.target === right.target
      : compareDecimals(left.target, right.target) === 0)
  );
}

interface OverfittingValue {
  readonly splits: number | string;
  readonly overfit: number | string;
  readonly probability: string;
}

/** The line of the probability of overfitting of the grid, and the warning from 50 % on. */
export function overfittingText(overfitting: OverfittingValue | null): {
  text: string;
  warning: string | null;
} {
  if (overfitting === null) {
    return { text: 'Вероятность подгонки сетки: мало данных для разбиений', warning: null };
  }

  return {
    text: `Вероятность подгонки сетки: ${roundDecimal(shiftDecimal(overfitting.probability, 2), 0)} % (${overfitting.overfit} из ${overfitting.splits} разбиений)`,
    warning:
      compareDecimals(overfitting.probability, '0.5') >= 0
        ? 'лучшее на одной половине дней правило сетки чаще не лучше середины на другой — выбор по ней ненадёжен'
        : null,
  };
}
