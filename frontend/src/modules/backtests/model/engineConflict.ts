const versions = /engine version (\d+).*engine version (\d+)/s;

/** The engine versions the API names when it refuses the details of a backtest computed by another engine. */
export function engineConflict(detail: string): { computed: string; current: string } | undefined {
  const match = versions.exec(detail);

  return match ? { computed: match[1]!, current: match[2]! } : undefined;
}

/** The reason the details of a result are not shown, for the owner. */
export function whyDetailsUnavailable(detail: string): string {
  const versions = engineConflict(detail);

  return versions
    ? `бэктест посчитан движком v${versions.computed}, а API работает с движком v${versions.current}.`
    : detail;
}
