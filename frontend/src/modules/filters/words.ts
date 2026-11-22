import type { FilterCondition, FilterField } from '@/api/filters';

/** The signs of the operators of a condition. */
export const operatorSigns: Record<string, string> = {
  gt: '>',
  ge: '≥',
  lt: '<',
  le: '≤',
  eq: '=',
  between: 'между',
};

/** The operators of a field: a number takes all, a field with a set of values only «равно». */
export function operatorsOf(field: FilterField | undefined): string[] {
  return field === undefined || field.type === 'number' ? Object.keys(operatorSigns) : ['eq'];
}

const valueWords: Record<string, string> = {
  long: 'лонг',
  short: 'шорт',
  true: 'да',
  false: 'нет',
};

/** A value of a field in words: the side as «шорт», a boolean as «да». */
export function valueWord(value: string | null | undefined): string {
  return value === null || value === undefined ? '' : (valueWords[value] ?? value);
}

/** The name of a field with its unit: «Фандинг, %», «Оборот 24 ч, млн USDT». */
export function fieldWords(field: FilterField | undefined, name: string): string {
  if (field === undefined) {
    return name;
  }

  return field.unit ? `${field.title}, ${field.unit}` : field.title;
}

/** A condition in words: «Фандинг, % ≥ 0.03», «Изменение 24 ч, % между -2 и 2». */
export function conditionWords(condition: FilterCondition, fields: readonly FilterField[]): string {
  const field = fields.find((candidate) => candidate.name === condition.field);
  const sign = operatorSigns[condition.operator] ?? condition.operator;
  const value =
    condition.operator === 'between'
      ? `${valueWord(condition.value)} и ${valueWord(condition.to)}`
      : valueWord(condition.value);
  return `${fieldWords(field, condition.field)} ${sign} ${value}`;
}

/** The conditions of a filter joined by «и». */
export function filterWords(
  conditions: readonly FilterCondition[],
  fields: readonly FilterField[],
): string {
  return conditions.map((condition) => conditionWords(condition, fields)).join(' и ');
}
