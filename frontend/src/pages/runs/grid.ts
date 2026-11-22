/** The most sets a run takes (spec strategy-backtest «Наборы значений»). */
export const maxSets = 200;

/** The values of a parameter in its field: a list through commas, each value without the spaces around it. */
export function valuesOf(text: string): string[] {
  return text
    .split(',')
    .map((value) => value.trim())
    .filter((value) => value !== '');
}

/**
 * How many sets the values and the timeframes give, by the rule of the server (change add-strategy-backtest, design.md,
 * decision 3; `BacktestGrid.Count`): one set for each combination of a value of every parameter that has values and a
 * timeframe. A parameter without values counts once; a timeframe given twice counts once, a value given twice twice.
 */
export function setCount(
  values: readonly (readonly string[])[],
  timeframes: readonly string[],
): number {
  return values
    .filter((parameter) => parameter.length > 0)
    .reduce((count, parameter) => count * parameter.length, new Set(timeframes).size);
}
