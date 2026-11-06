import {
  compareDecimals,
  divideDecimals,
  multiplyDecimals,
  shiftDecimal,
  subtractDecimals,
} from '@/shared/format/decimal';
import { roundDecimal, signed } from '@/shared/format/format';

/** A row of the table of probability: the mean promise, the share of targets and its 95 % Wilson interval, none under 30 cases. Fractions. */
export interface ProbabilityRow {
  readonly promised: string;
  readonly actual: string;
  readonly low: string | null;
  readonly high: string | null;
}

export interface DifferenceView {
  /** «сбылось − обещали» in whole percentage points with its sign: «−13 п.п.». */
  readonly text: string;
  /** Grey while the promise is inside the interval, yellow past it, red past two half-widths of it; grey without an interval. */
  readonly tone: 'muted' | 'warn' | 'err';
}

export function differenceView(row: ProbabilityRow): DifferenceView {
  const difference = subtractDecimals(row.actual, row.promised);
  const points = roundDecimal(shiftDecimal(difference, 2), 0);
  const text = `${signed(points).replace('-', '−')} п.п.`;

  if (row.low === null || row.high === null) {
    return { text, tone: 'muted' };
  }

  const inside =
    compareDecimals(row.promised, row.low) >= 0 && compareDecimals(row.promised, row.high) <= 0;
  const far = compareDecimals(difference.replace('-', ''), subtractDecimals(row.high, row.low)) > 0;

  return { text, tone: inside ? 'muted' : far ? 'err' : 'warn' };
}

export interface BrierView {
  /** «0.207 против 0.219»: the score of the buckets against the score of the share of targets of the category. */
  readonly scores: string;
  /** How much lower the score of the buckets is, in percent of the score without them; lower is better. */
  readonly verdict: string;
}

export function brierView(brier: string, base: string): BrierView {
  const gain = divideDecimals(multiplyDecimals(subtractDecimals(base, brier), '100'), base, 1);
  const scores = `${roundDecimal(brier, 3)} против ${roundDecimal(base, 3)}`;

  if (gain === '0') {
    return { scores, verdict: 'не лучше общей доли целей' };
  }

  return {
    scores,
    verdict: gain.startsWith('-') ? `хуже на ${gain.slice(1)} %` : `лучше на ${gain} %`,
  };
}

const bound = (value: string, unit: '%' | 'R') =>
  (unit === '%' ? shiftDecimal(value, 2) : value).replace('-', '−');

/** The name of a row of promises from `from` up to `to`: «< 20 %», «20–30 %», «≥ 60 %», «−0.2…0 R». */
export function rangeLabel(from: string | null, to: string | null, unit: '%' | 'R'): string {
  if (from === null) {
    return `< ${bound(to!, unit)} ${unit}`;
  }

  if (to === null) {
    return `≥ ${bound(from, unit)} ${unit}`;
  }

  return `${bound(from, unit)}${unit === '%' ? '–' : '…'}${bound(to, unit)} ${unit}`;
}
