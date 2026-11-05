import { isDecimalText } from '@/shared/api/jsonNumber';

/** A condition as the owner types it: percent for funding and probability, the numbers themselves otherwise. */
export interface Draft {
  field: string;
  operator: string;
  value: string;
  to: string;
}

export interface FieldInfo {
  readonly code: string;
  readonly operators: readonly string[];
  readonly choices: readonly string[] | null;
}

/** A new condition on a field: its first operator, and the first choice of a choice field so that it is never empty. */
export const draftOf = (field: FieldInfo): Draft => ({
  field: field.code,
  operator: field.operators[0]!,
  value: field.choices?.[0] ?? '',
  to: '',
});

/**
 * The condition moved from one field to another: it keeps the operator and the number the new field takes, nothing else;
 * the «=» of a choice field is no comparison to carry over to numbers.
 */
export function switchField(draft: Draft, from: FieldInfo, next: FieldInfo): Draft {
  const kept =
    next.choices === null ? isDecimalText(draft.value) : next.choices.includes(draft.value);

  return {
    field: next.code,
    operator:
      next.operators.includes(draft.operator) && (from.choices === null) === (next.choices === null)
        ? draft.operator
        : next.operators[0]!,
    value: kept ? draft.value : (next.choices?.[0] ?? ''),
    to: next.choices === null ? draft.to : '',
  };
}
