import { type FieldErrors, layOutProblems, type ProblemErrors } from '../../../shared/api/problems';

const fields = new Set([
  'from',
  'to',
  'period',
  'parameters',
  'searchMode',
  'simulation.balance',
  'simulation.limitFeePercent',
  'simulation.marketFeePercent',
  'simulation.slippagePercent',
  'simulation.limitFillMode',
]);

/**
 * The errors of a backtest request by form field. `parameters` also carries the combinations over the limit, and
 * `parameters.<name>` is the field of one parameter.
 */
export function backtestFieldErrors(errors: ProblemErrors): FieldErrors {
  return layOutProblems(errors, (key) => fields.has(key) || key.startsWith('parameters.'));
}
