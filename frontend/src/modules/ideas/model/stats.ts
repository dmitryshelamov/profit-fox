import type { Schemas } from '@/shared/api/client';
import { isDecimalText } from '@/shared/api/jsonNumber';
import { compareDecimals, multiplyDecimals, shiftDecimal } from '@/shared/format/decimal';
import { roundDecimal, showDecimal } from '@/shared/format/format';

type Bucket = Schemas['BucketStatisticsResponse'];

export type Badge = '▲' | '≈' | '▼';

const badges: Record<string, Badge> = { up: '▲', flat: '≈', down: '▼' };

/** The symbol of the code of a badge of a bucket: `up` is ▲, `flat` ≈, `down` ▼. */
export const badgeSymbol = (code: string): Badge => badges[code]!;

export interface ProbabilityView {
  readonly enough: boolean;
  /** The probability in whole percent; `null` while there is too little data. */
  readonly percent: string | null;
  readonly cases: number;
  /** The badge of the bucket by the interval of its expectancy; `null` while there is too little data. */
  readonly badge: Badge | null;
}

/** What the probability column of an idea shows: its bucket, or «мало данных» for fewer than 30 cases or no case. */
export function probabilityView(bucket: Bucket | null): ProbabilityView {
  if (bucket === null || !bucket.enoughData) {
    return { enough: false, percent: null, cases: Number(bucket?.cases ?? 0), badge: null };
  }

  return {
    enough: true,
    percent: roundDecimal(shiftDecimal(bucket.probability!, 2), 0),
    cases: Number(bucket.cases),
    badge: badgeSymbol(bucket.badge!),
  };
}

/** The break-even of an idea after the fees, rounded up to a whole percent, or that its target does not cover the fees. */
export function breakEvenText(breakEven: string | null): string {
  if (breakEven === null) {
    return 'не окупает комиссии';
  }

  const [whole = '0', fraction = ''] = shiftDecimal(breakEven, 2).split('.');

  return `нужно ≥ ${/[1-9]/.test(fraction) ? BigInt(whole) + 1n : whole} %`;
}

/** The shares of targets, stops and expired cases of a bucket in whole percent, for the hint of its probability; none without a case. */
export function outcomeShares(bucket: Bucket | null): string | null {
  if (bucket === null) {
    return null;
  }

  const percent = (count: string | number) =>
    Math.round((Number(count) * 100) / Number(bucket.cases));

  return `Цель ${percent(bucket.targets)} % · Стоп ${percent(bucket.stops)} % · Истекла ${percent(bucket.expired)} %`;
}

/** The expectancy of the bucket in R with a sign, or a dash for a bucket without a case. */
export function expectancyText(bucket: Bucket | null): string {
  return bucket === null ? '—' : `${showDecimal(bucket.expectancy, 2, true)} R`;
}

export interface MoneyOutcome {
  readonly profit: string;
  readonly loss: string;
}

type Shares = Pick<Schemas['IdeaResponse'], 'profitPerUsdt' | 'lossPerUsdt'>;

const isPositive = (amount: string) => isDecimalText(amount) && compareDecimals(amount, '0') > 0;

/** The profit and the loss of an idea in USDT for the amount of a deal (its shares on 1 USDT, after the fees); none while the amount is not a positive number. */
export function moneyOutcome(idea: Shares, amount: string): MoneyOutcome | null {
  if (!isPositive(amount)) {
    return null;
  }

  return {
    profit: showDecimal(multiplyDecimals(idea.profitPerUsdt, amount), 2, true),
    loss: `−${showDecimal(multiplyDecimals(idea.lossPerUsdt, amount), 2)}`,
  };
}
