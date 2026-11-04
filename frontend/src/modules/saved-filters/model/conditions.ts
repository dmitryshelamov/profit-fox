import { isDecimalText } from '@/shared/api/jsonNumber';
import { shiftDecimal } from '@/shared/format/decimal';
import { groupDigits } from '@/shared/format/format';

import type { Condition } from './apply';

const fieldLabels: Record<string, string> = {
  price: 'Цена',
  change5m: 'Изменение 5м',
  change15m: 'Изменение 15м',
  change1h: 'Изменение 1ч',
  change4h: 'Изменение 4ч',
  change24h: 'Изменение 24ч',
  turnover1h: 'Оборот 1ч',
  turnover24h: 'Оборот 24ч',
  openInterest: 'OI',
  openInterestChange1h: 'ΔOI 1ч',
  openInterestChange24h: 'ΔOI 24ч',
  fundingRate: 'Фандинг',
  spread: 'Спред',
  riskReward: 'R:R',
  probability: 'Вероятность',
  expectancy: 'Ожидание',
  cases: 'Случаев (N)',
  aboveBaseline: 'Выше базовой линии',
};

const operatorLabels: Record<string, string> = {
  '>': '>',
  '>=': '≥',
  '<': '<',
  '<=': '≤',
  '=': '=',
  between: 'между',
  above_baseline: 'выше базовой',
};

/** These two are fractions in the data (0.0003 is 0.03 %), but the owner types percent. */
const typedAsPercent = new Set(['fundingRate', 'probability']);

const percentFields = new Set([
  'change5m',
  'change15m',
  'change1h',
  'change4h',
  'change24h',
  'openInterestChange1h',
  'openInterestChange24h',
  'spread',
]);
const usdtFields = new Set(['price', 'turnover1h', 'turnover24h', 'openInterest']);

export const fieldLabel = (code: string) => fieldLabels[code] ?? code;
export const operatorLabel = (code: string) => operatorLabels[code] ?? code;

export function unitLabel(code: string): string {
  if (usdtFields.has(code)) {
    return 'USDT';
  }

  if (percentFields.has(code) || typedAsPercent.has(code)) {
    return '%';
  }

  return code === 'expectancy' ? 'R' : '';
}

/** The stored value as the owner types it: percent for funding and probability, the number itself otherwise. */
export function toTyped(field: string, stored: string): string {
  return typedAsPercent.has(field) ? shiftDecimal(stored, 2) : stored;
}

/** What the owner typed as the stored value; a text that is not a number is kept for the server to refuse. */
export function fromTyped(field: string, typed: string): string {
  return typedAsPercent.has(field) && isDecimalText(typed) ? shiftDecimal(typed, -2) : typed;
}

function shown(field: string, stored: string | null): string {
  return groupDigits(toTyped(field, stored ?? ''));
}

/** A condition as one phrase: «Изменение 1ч > 3 %». */
export function conditionText(condition: Condition): string {
  const field = condition.field!;
  const unit = unitLabel(field);
  const tail = unit === '' ? '' : ` ${unit}`;

  if (condition.operator === 'above_baseline') {
    return fieldLabel(field);
  }

  if (condition.operator === 'between') {
    return `${fieldLabel(field)} между ${shown(field, condition.value)} и ${shown(field, condition.to)}${tail}`;
  }

  return `${fieldLabel(field)} ${operatorLabel(condition.operator!)} ${shown(field, condition.value)}${tail}`;
}

/** The conditions that are filled in enough to test rows now, as the live counter of the editor needs. */
export function readyConditions(conditions: readonly Condition[]): Condition[] {
  const number = (text: string | null) => text !== null && isDecimalText(text);

  return conditions.filter(
    (condition) =>
      condition.operator === 'above_baseline' ||
      (number(condition.value) && (condition.operator !== 'between' || number(condition.to))),
  );
}
