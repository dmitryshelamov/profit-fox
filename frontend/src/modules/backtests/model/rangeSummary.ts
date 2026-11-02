interface Spec {
  readonly min: unknown;
  readonly max: unknown;
  readonly step: unknown;
}

/** The ranges of a backtest's parameters as `fast 5…15 / 5 · slow 20…40 / 10`; none when every value is single. */
export function rangeSummary(parameters: Readonly<Record<string, unknown>>): string | undefined {
  const ranges = Object.entries(parameters)
    .filter(([, spec]) => typeof spec === 'object' && spec !== null)
    .map(([name, spec]) => {
      const { min, max, step } = spec as Spec;

      return `${name} ${String(min)}…${String(max)} / ${String(step)}`;
    });

  return ranges.length > 0 ? ranges.join(' · ') : undefined;
}

/** The names of the parameters given as ranges, in the order of the parameters. */
export function rangedNames(parameters: Readonly<Record<string, unknown>>): string[] {
  return Object.entries(parameters)
    .filter(([, spec]) => typeof spec === 'object' && spec !== null)
    .map(([name]) => name);
}
