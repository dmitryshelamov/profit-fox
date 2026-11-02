export interface Range {
  readonly min: string;
  readonly max: string;
  readonly step: string;
}

export type RangeCount =
  { readonly count: bigint } | { readonly problem: 'step' | 'order' | 'divisibility' };

interface Scaled {
  readonly digits: bigint;
  readonly scale: number;
}

const duration = /^(?:(\d+)\.)?(\d+):(\d+):(\d+)$/;

/** A number or a `TimeSpan` such as `1.02:00:00` as an integer with a decimal scale; a duration counts in seconds. */
function scaled(value: string): Scaled {
  const span = duration.exec(value);

  if (span) {
    // Only the days may be missing from a duration.
    const [, days, hours, minutes, seconds] = span as unknown as [
      string,
      string | undefined,
      string,
      string,
      string,
    ];

    return {
      digits:
        BigInt(days ?? 0) * 86400n +
        BigInt(hours) * 3600n +
        BigInt(minutes) * 60n +
        BigInt(seconds),
      scale: 0,
    };
  }

  const [whole = '', fraction = ''] = value.split('.');

  return { digits: BigInt(whole + fraction), scale: fraction.length };
}

function atScale({ digits, scale }: Scaled, target: number): bigint {
  return digits * 10n ** BigInt(target - scale);
}

/** How many values a range yields: `min`, `min + step`, … up to `max`. Exact, so large grids keep every digit. */
export function rangeCount(range: Range): RangeCount {
  const lowest = scaled(range.min);
  const highest = scaled(range.max);
  const by = scaled(range.step);
  const scale = Math.max(lowest.scale, highest.scale, by.scale);
  const min = atScale(lowest, scale);
  const max = atScale(highest, scale);
  const step = atScale(by, scale);

  if (step <= 0n) {
    return { problem: 'step' };
  }

  if (min > max) {
    return { problem: 'order' };
  }

  if ((max - min) % step !== 0n) {
    return { problem: 'divisibility' };
  }

  return { count: (max - min) / step + 1n };
}

/** The number of backtests of a grid: the product of the counts of its ranges. */
export function combinationsOf(counts: readonly bigint[]): bigint {
  return counts.reduce((product, count) => product * count, 1n);
}
