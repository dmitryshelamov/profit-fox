import type { Schemas } from '@/shared/api/client';
import { isDecimalText } from '@/shared/api/jsonNumber';
import { compareDecimals, multiplyDecimals, shiftDecimal } from '@/shared/format/decimal';
import { roundDecimal, showDecimal } from '@/shared/format/format';

import { bucketCasesText } from './labels';

type Bucket = Schemas['BucketStatisticsResponse'];
type Numbers = Schemas['IdeaNumbersResponse'];

export type Badge = '▲' | '≈' | '▼';

const badges: Record<string, Badge> = { up: '▲', flat: '≈', down: '▼' };

/** The symbol of the code of a badge of a bucket or an idea: `up` is ▲, `flat` ≈, `down` ▼. */
export const badgeSymbol = (code: string): Badge => badges[code]!;

export interface ProbabilityView {
  readonly enough: boolean;
  /** The probability of the idea in whole percent; `null` while there is too little data. */
  readonly percent: string | null;
  /** The cases of the bucket of the idea. */
  readonly cases: number;
}

/** What the probability column of an idea shows: its own probability with N of its bucket, or «мало данных» while it has none. */
export function probabilityView(numbers: Numbers, bucket: Bucket | null): ProbabilityView {
  const cases = Number(bucket?.cases ?? 0);

  return numbers.probability === null
    ? { enough: false, percent: null, cases }
    : { enough: true, percent: percentOf(numbers.probability), cases };
}

/** The badge of the idea by the interval of its own expectancy; `null` without one. */
export function ideaBadge(numbers: Numbers): Badge | null {
  return numbers.badge === null ? null : badgeSymbol(numbers.badge);
}

const bandLabels: Record<string, string> = {
  up_to_2: 'до 2',
  from_2_to_3: '2–3',
  from_3_to_6: '3–6',
  from_6: 'от 6',
};

/**
 * The hint of the probability of an idea: for «Уровни» first the probability of its bucket times the multiplier of the band of its reward
 * to risk, «корзина 22 % × 0.68 по R:R 3–6», then the shares of the outcomes of the bucket; none without a bucket.
 */
export function probabilityHint(numbers: Numbers, bucket: Bucket | null): string | null {
  const shares = outcomeShares(bucket);

  if (shares === null || numbers.band === null || bucket!.probability === null) {
    return shares;
  }

  return `корзина ${percentOf(bucket!.probability)} % × ${showDecimal(numbers.multiplier, 2)} по R:R ${bandLabels[numbers.band]} · ${shares}`;
}

const percentOf = (share: string) => roundDecimal(shiftDecimal(share, 2), 0);

/** The break-even of an idea after the fees, rounded up to a whole percent, or that its target does not cover the fees. */
export function breakEvenText(breakEven: string | null): string {
  return breakEven === null ? 'не окупает комиссии' : `нужно ≥ ${percentUp(breakEven)} %`;
}

/** A share in whole percent rounded up, as a break-even is: 0.2957 is 30. */
export function percentUp(share: string): string {
  const [whole = '0', fraction = ''] = shiftDecimal(share, 2).split('.');

  return String(/[1-9]/.test(fraction) ? BigInt(whole) + 1n : BigInt(whole));
}

/**
 * The shares of targets, stops and expired cases of a bucket in whole percent, for the hint of its probability, and of a bucket of
 * «Откат» or «Стоп-вход» the share of its signals that entered and the expectancy per signal; none without a case.
 */
export function outcomeShares(bucket: Bucket | null): string | null {
  if (bucket === null) {
    return null;
  }

  const percent = (count: string | number) =>
    Math.round((Number(count) * 100) / Number(bucket.cases));

  const shares = `Цель ${percent(bucket.targets)} % · Стоп ${percent(bucket.stops)} % · Истекла ${percent(bucket.expired)} %`;

  return bucket.signalExpectancy === null
    ? shares
    : `${shares} · входов ${roundDecimal(shiftDecimal(bucket.entryShare!, 2), 0)} % · на сигнал ${showDecimal(bucket.signalExpectancy, 2, true)} R`;
}

/**
 * The own numbers of a bucket beside those of the model, for the hint of its expectancy: its cases, its own share of targets (none under 30
 * own cases), its own expectancy and the weight of the parent in the model; none without the numbers of the model.
 */
export function ownNumbers(bucket: Bucket | null): string | null {
  if (bucket === null || bucket.parentWeight === null) {
    return null;
  }

  const share =
    bucket.ownProbability === null
      ? ''
      : ` · ${roundDecimal(shiftDecimal(bucket.ownProbability, 2), 0)} %`;

  return `в самой корзине: ${bucketCasesText(Number(bucket.cases))}${share} · ${showDecimal(bucket.ownExpectancy, 2, true)} R · по родителю ${roundDecimal(shiftDecimal(bucket.parentWeight, 2), 0)} %`;
}

/** The expectancy of the idea in R with a sign, or a dash for an idea without one. */
export function expectancyText(numbers: Numbers): string {
  return numbers.expectancy === null ? '—' : `${showDecimal(numbers.expectancy, 2, true)} R`;
}

/** The expectancy of the idea on 100 USDT of the amount of a deal, a share of the amount and not a forecast of money; none without one. */
export function perHundredText(numbers: Numbers): string | null {
  return numbers.expectancyPerUsdt === null
    ? null
    : `на 100 USDT: ${showDecimal(multiplyDecimals(numbers.expectancyPerUsdt, '100'), 2, true)}`;
}

/**
 * The hint of the expectancy of an idea: what it is made of, «без издержек −0.02 R − издержки 0.70 R», and on the next line the own numbers
 * of its bucket; none without a bucket.
 */
export function expectancyHint(numbers: Numbers, bucket: Bucket | null): string | null {
  const own = ownNumbers(bucket);
  const made =
    numbers.expectancy === null
      ? null
      : `без издержек ${showDecimal(bucket!.grossExpectancy!, 2, true)} R − издержки ${showDecimal(numbers.costs, 2)} R`;

  return [made, own].filter((line) => line !== null).join('\n') || null;
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
