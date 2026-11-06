import { sideLabel, typeLabel } from '@/modules/ideas';
import { setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';
import { isDecimalText } from '@/shared/api/jsonNumber';
import { shiftDecimal } from '@/shared/format/decimal';
import { groupDigits } from '@/shared/format/format';

import type { Condition } from './savedFilter';

const fieldLabels: Record<string, string> = {
  category: 'Категория',
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
  setup: 'Сетап',
  ideaSetup: 'Сетап',
  type: 'Тип идеи',
  interval: 'ТФ',
  side: 'Сторона',
  riskReward: 'R:R',
  probability: 'Вероятность',
  expectancy: 'Ожидание',
};

/** In a phrase a field of the idea says whose it is, since «Сетап» of a pair is another field. */
const phraseLabels: Record<string, string> = {
  ideaSetup: 'Сетап идеи',
  type: 'Тип идеи',
  interval: 'ТФ идеи',
  side: 'Сторона идеи',
};

const choiceLabels: Record<string, (code: string) => string> = {
  setup: setupLabel,
  ideaSetup: setupLabel,
  type: typeLabel,
  interval: intervalLabel,
  side: sideLabel,
};

const operatorLabels: Record<string, string> = {
  '>': '>',
  '>=': '≥',
  '<': '<',
  '<=': '≤',
  '=': '=',
  between: 'между',
  reliably_positive: 'надёжно > 0',
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
const usdtFields = new Set(['turnover1h', 'turnover24h', 'openInterest']);

export const isChoiceField = (code: string) => code === 'category' || code in choiceLabels;

export const fieldLabel = (code: string) => fieldLabels[code] ?? code;
/** The name of a code of a choice field; the category is shown as it is. */
export const choiceLabel = (field: string, code: string) => choiceLabels[field]?.(code) ?? code;
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

/** What the owner typed as the stored value, a decimal comma taken for a point; a text that is not a number is kept as it is. */
export function fromTyped(field: string, typed: string): string {
  const text = typed.replace(',', '.');

  return typedAsPercent.has(field) && isDecimalText(text) ? shiftDecimal(text, -2) : text;
}

function shown(field: string, stored: string | null): string {
  return groupDigits(toTyped(field, stored ?? ''));
}

/** A condition as one phrase: «Изменение 1ч > 3 %». */
export function conditionText(condition: Condition): string {
  const field = condition.field!;
  const unit = unitLabel(field);
  const tail = unit === '' ? '' : ` ${unit}`;

  const label = phraseLabels[field] ?? fieldLabel(field);

  if (condition.operator === 'reliably_positive') {
    return `${label} ${operatorLabel(condition.operator)}`;
  }

  if (isChoiceField(field)) {
    return `${label} ${operatorLabel(condition.operator!)} ${choiceLabel(field, condition.value!)}`;
  }

  if (condition.operator === 'between') {
    return `${label} между ${shown(field, condition.value)} и ${shown(field, condition.to)}${tail}`;
  }

  return `${label} ${operatorLabel(condition.operator!)} ${shown(field, condition.value)}${tail}`;
}

/** The conditions that are filled in enough to test rows now, as the live counter of the editor needs. */
export function readyConditions(conditions: readonly Condition[]): Condition[] {
  const number = (text: string | null) => text !== null && isDecimalText(text);

  return conditions.filter(
    (condition) =>
      condition.operator === 'reliably_positive' ||
      (isChoiceField(condition.field!) && condition.value !== null && condition.value !== '') ||
      (number(condition.value) && (condition.operator !== 'between' || number(condition.to))),
  );
}
