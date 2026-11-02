import { type FieldErrors, layOutProblems, type ProblemErrors } from '../../../shared/api/problems';

const fields = new Set([
  'name',
  'strategyId',
  'category',
  'symbol',
  'balance',
  'interval',
  'exchangeAccountId',
]);

/** The errors of a bot request by form field; `parameterValues.<name>` is the field of one parameter. */
export function botFieldErrors(errors: ProblemErrors): FieldErrors {
  return layOutProblems(errors, (key) => fields.has(key) || key.startsWith('parameterValues.'));
}
