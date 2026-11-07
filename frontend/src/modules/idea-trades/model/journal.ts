import { badgeSymbol, breakEvenText, counted, statusLabel } from '@/modules/ideas';
import type { Schemas } from '@/shared/api/client';
import { shiftDecimal } from '@/shared/format/decimal';
import { roundDecimal, showDecimal } from '@/shared/format/format';

type Promise = Schemas['IdeaPromiseResponse'];
type Outcome = Schemas['IdeaOutcomeResponse'];
type Totals = Schemas['IdeaJournalTotalsResponse'];

const dash = '—';

/** The time in a trade: «2 ч 15 мин», «1 д 3 ч», «45 мин»; a dash for none. */
export function durationText(seconds: number | string | null): string {
  if (seconds === null) {
    return dash;
  }

  const minutes = Math.floor(Number(seconds) / 60);

  if (minutes < 1) {
    return '< 1 мин';
  }

  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const rest = minutes % 60;

  if (days > 0) {
    return hours > 0 ? `${days} д ${hours} ч` : `${days} д`;
  }

  if (hours > 0) {
    return rest > 0 ? `${hours} ч ${rest} мин` : `${hours} ч`;
  }

  return `${rest} мин`;
}

const rText = (value: string) => `${showDecimal(value, 2, true)} R`;

/** What the idea promised at the entry: the badge, the expectancy in R and the cases of the bucket; a dash when there is no bucket. */
export function promiseText(promise: Promise | null): string {
  if (promise === null || promise.expectancy === null) {
    return dash;
  }

  const badge = promise.badge === null ? '' : `${badgeSymbol(promise.badge)} `;

  return `${badge}${rText(promise.expectancy)} · N ${promise.cases}`;
}

/** The probability of the bucket, the break-even and the reward to risk after the fees, for the hint of the promise. */
export function promiseHint(promise: Promise): string {
  const probability =
    promise.probability === null
      ? '—'
      : `${roundDecimal(shiftDecimal(promise.probability, 2), 0)} %`;
  const breakEven =
    promise.breakEven === null
      ? 'не окупает комиссии'
      : `безубыток: ${breakEvenText(promise.breakEven)}`;
  const separator = promise.breakEven === null ? ', ' : ' · ';

  return `Вероятность ${probability}${separator}${breakEven} · R:R ${roundDecimal(promise.netRiskReward, 2)}`;
}

/** How the idea of a trade ended: its status and, when it entered, its result in R; a dash for none. */
export function ideaOutcomeText(outcome: Outcome | null): string {
  if (outcome === null) {
    return dash;
  }

  return outcome.result === null
    ? statusLabel(outcome.status)
    : `${statusLabel(outcome.status)} ${rText(outcome.result)}`;
}

const share = (count: number, all: number) => `${count} (${Math.round((count * 100) / all)} %)`;

/** The trades closed at the target, at the stop, manually and at expiry, each with its share of the four; a dash when there are none. */
export function outcomeCountsText(totals: Totals): string {
  const targets = Number(totals.targets);
  const stops = Number(totals.stops);
  const manual = Number(totals.manual);
  const expired = Number(totals.expired);
  const all = targets + stops + manual + expired;

  return all === 0
    ? dash
    : `по цели ${share(targets, all)} · по стопу ${share(stops, all)} · вручную ${share(manual, all)} · по сроку ${share(expired, all)}`;
}

/** How many trades closed at the target, at the stop, manually and at expiry, with the word that goes with the count. */
export function closedCountText(totals: Totals): string {
  return counted(
    Number(totals.targets) + Number(totals.stops) + Number(totals.manual) + Number(totals.expired),
    'сделка',
    'сделки',
    'сделок',
  );
}

/** The cancelled and the failed trades, which are not in the other totals; none when there are none. */
export function excludedText(totals: Totals): string | null {
  const parts = [
    Number(totals.cancelled) > 0 ? `отменено ${totals.cancelled}` : null,
    Number(totals.failed) > 0 ? `с ошибкой ${totals.failed}` : null,
  ].filter((part) => part !== null);

  return parts.length === 0 ? null : parts.join(' · ');
}

/** The mean result in R with its 95 % interval; the mean alone for one trade, a dash for none. */
export function meanText(totals: Totals): string {
  if (totals.meanR === null) {
    return dash;
  }

  return totals.meanRLow === null || totals.meanRHigh === null
    ? rText(totals.meanR)
    : `${rText(totals.meanR)} (95 %: ${showDecimal(totals.meanRLow, 2)} … ${showDecimal(totals.meanRHigh, 2, true)})`;
}

/** The sum of the results in USDT and the fees. */
export function moneyText(totals: Totals): string {
  return `${showDecimal(totals.result, 2, true)} USDT · комиссии ${showDecimal(totals.fees, 2)} USDT`;
}

/** The expectancy the trades were promised against the result they got, with the number of them; a dash when none had a promise. */
export function promiseComparisonText(comparison: Totals['promise']): string {
  return comparison.count === 0 || comparison.promised === null || comparison.got === null
    ? dash
    : `обещали ${rText(comparison.promised)} → получили ${rText(comparison.got)} · N ${comparison.count}`;
}

/** The result of the ideas of the trades against the result of the trades, with the number of them; a dash when none had one. */
export function ideaComparisonText(comparison: Totals['ideas']): string {
  return comparison.count === 0 || comparison.ideas === null || comparison.trades === null
    ? dash
    : `идеи ${rText(comparison.ideas)} → сделки ${rText(comparison.trades)} · N ${comparison.count}`;
}
