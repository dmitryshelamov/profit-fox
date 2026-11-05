import type { Schemas } from '@/shared/api/client';
import { isDecimalText } from '@/shared/api/jsonNumber';
import { compareDecimals, multiplyDecimals, shiftDecimal } from '@/shared/format/decimal';
import { roundDecimal, showDecimal } from '@/shared/format/format';

type Bucket = Schemas['BucketStatisticsResponse'];

export type BaselineBadge = '▲' | '≈' | '▼';

/** ▲ when the whole interval of the probability is above the baseline, ▼ when it is all below it, otherwise ≈. */
export function baselineBadge(low: string, high: string, baseline: string): BaselineBadge {
  if (compareDecimals(low, baseline) > 0) {
    return '▲';
  }

  return compareDecimals(high, baseline) < 0 ? '▼' : '≈';
}

export interface ProbabilityView {
  readonly enough: boolean;
  /** The probability in whole percent; `null` while there is too little data. */
  readonly percent: string | null;
  readonly cases: number;
  readonly badge: BaselineBadge | null;
}

/** What the probability column of an idea shows: its bucket, or «мало данных» for fewer than 30 cases or no case. */
export function probabilityView(bucket: Bucket | null, baseline: string): ProbabilityView {
  // A bucket of enough cases that all expired has no share either; the interval comes with the share.
  if (bucket === null || !bucket.enoughData || bucket.probability === null) {
    return { enough: false, percent: null, cases: Number(bucket?.cases ?? 0), badge: null };
  }

  return {
    enough: true,
    percent: roundDecimal(shiftDecimal(bucket.probability, 2), 0),
    cases: Number(bucket.cases),
    badge: baselineBadge(bucket.probabilityLow!, bucket.probabilityHigh!, baseline),
  };
}

/** The expectancy of the bucket in R with a sign, or a dash for a bucket without a case. */
export function expectancyText(bucket: Bucket | null): string {
  return bucket === null ? '—' : `${showDecimal(bucket.expectancy, 2, true)} R`;
}

export interface RiskOutcome {
  readonly profit: string;
  readonly loss: string;
}

/** The profit (R:R × risk) and the loss (minus the risk) of an idea in USDT; none while the risk is not a positive number. */
export function riskOutcome(riskReward: string, risk: string): RiskOutcome | null {
  if (!isDecimalText(risk) || compareDecimals(risk, '0') <= 0) {
    return null;
  }

  return {
    profit: showDecimal(multiplyDecimals(riskReward, risk), 1, true),
    loss: `−${showDecimal(risk, 1)}`,
  };
}
