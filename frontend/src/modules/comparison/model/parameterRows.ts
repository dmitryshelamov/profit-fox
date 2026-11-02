const MISSING = '—';

/** Parameter values by result, as rows: every name met in any result, in order of first appearance. */
export function parameterRows(
  results: readonly Readonly<Record<string, string>>[],
): { name: string; values: string[] }[] {
  const names = new Set(results.flatMap((parameters) => Object.keys(parameters)));

  return [...names].map((name) => ({
    name,
    values: results.map((parameters) => parameters[name] ?? MISSING),
  }));
}
