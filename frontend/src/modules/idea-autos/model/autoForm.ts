import type { Schemas } from '@/shared/api/client';
import { isDecimalText, jsonNumberOf } from '@/shared/api/jsonNumber';
import { type FieldErrors, layOutProblems, type ProblemErrors } from '@/shared/api/problems';

/** The auto as the form holds it: every number as typed, the choices as codes, an empty text for none. */
export interface AutoFormValues {
  readonly name: string;
  readonly accountId: string;
  readonly amount: string;
  /** The budget of the auto; an empty text is no budget. */
  readonly budget: string;
  readonly mode: string;
  readonly setups: readonly string[];
  readonly types: readonly string[];
  readonly intervals: readonly string[];
  readonly side: string;
  readonly category: string;
  readonly badge: string;
  readonly minRiskReward: string;
  readonly minCases: string;
}

/** The names of the fields of the body of an auto in the API and in the form. */
const fields: ReadonlySet<string> = new Set([
  'name',
  'accountId',
  'amount',
  'budget',
  'setups',
  'types',
  'intervals',
  'side',
  'category',
  'badge',
  'minRiskReward',
  'minCases',
]);

/** A new auto: the account, the amount and the lowest R:R of the settings of trading by ideas, no budget, no conditions, off. */
export function formOfDefaults(settings: {
  accountId: string | null;
  amount: string;
  minRiskReward: string;
}): AutoFormValues {
  return {
    name: '',
    accountId: settings.accountId ?? '',
    amount: settings.amount,
    budget: '',
    mode: 'off',
    setups: [],
    types: [],
    intervals: [],
    side: '',
    category: '',
    badge: '',
    minRiskReward: settings.minRiskReward,
    minCases: '0',
  };
}

export function formOfAuto(auto: Schemas['IdeaAutoResponse']): AutoFormValues {
  return {
    name: auto.name,
    accountId: auto.accountId,
    amount: auto.amount,
    budget: auto.budget ?? '',
    mode: auto.mode,
    setups: auto.setups,
    types: auto.types,
    intervals: auto.intervals,
    side: auto.side ?? '',
    category: auto.category ?? '',
    badge: auto.badge ?? '',
    minRiskReward: auto.minRiskReward,
    minCases: String(auto.minCases),
  };
}

/** A number the form holds as text; text that is no number goes out as it is, so the API names the field. */
const numberOf = (text: string) =>
  isDecimalText(text) ? (jsonNumberOf(text) as unknown as number) : text;

/** The body of a save: an empty choice or budget is none; the mode is not in it, it has a request of its own. */
export function bodyOfForm(form: AutoFormValues): Schemas['IdeaAutoBody'] {
  return {
    name: form.name,
    accountId: form.accountId,
    amount: numberOf(form.amount),
    budget: form.budget === '' ? null : numberOf(form.budget),
    setups: [...form.setups],
    types: [...form.types],
    intervals: [...form.intervals],
    side: form.side === '' ? null : form.side,
    category: form.category === '' ? null : form.category,
    badge: form.badge === '' ? null : form.badge,
    minRiskReward: numberOf(form.minRiskReward),
    minCases: numberOf(form.minCases),
  };
}

const names: readonly (readonly [keyof AutoFormValues, string])[] = [
  ['name', 'название'],
  ['accountId', 'аккаунт'],
  ['amount', 'сумма'],
  ['budget', 'бюджет'],
  ['mode', 'режим'],
  ['setups', 'сетапы'],
  ['types', 'типы идей'],
  ['intervals', 'ТФ'],
  ['side', 'сторона'],
  ['category', 'рынок'],
  ['badge', 'значок'],
  ['minRiskReward', 'минимальный R:R'],
  ['minCases', 'минимум кейсов'],
];

const sameValue = (left: string | readonly string[], right: string | readonly string[]) =>
  typeof left === 'string' || typeof right === 'string'
    ? left === right
    : left.length === right.length && left.every((item) => right.includes(item));

/** The names of the fields that differ from the saved auto, in the order of the form. */
export function changedFields(initial: AutoFormValues, current: AutoFormValues): string[] {
  return names.filter(([key]) => !sameValue(initial[key], current[key])).map(([, name]) => name);
}

/** «изменено: ТФ, минимальный R:R»; nothing when nothing changed. */
export const changedText = (changed: readonly string[]) =>
  changed.length === 0 ? '' : `изменено: ${changed.join(', ')}`;

/** The errors of the API laid out by the field of the form that failed. */
export function autoFieldErrors(errors: ProblemErrors): FieldErrors {
  return layOutProblems(errors, (key) => fields.has(key));
}
