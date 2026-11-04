export interface OpenInterestInput {
  readonly time: string;
  readonly value: string;
}

export interface FundingInput {
  readonly time: string;
  readonly rate: string;
}

export interface LinePoint {
  readonly time: number;
  readonly value: number;
}

export interface FundingBar extends LinePoint {
  readonly positive: boolean;
}

const secondsOf = (time: string) => Date.parse(time) / 1000;

/** Open interest in coins: decimal strings become numbers here, for drawing only. */
export function openInterestLine(points: readonly OpenInterestInput[]): LinePoint[] {
  return points.map((point) => ({ time: secondsOf(point.time), value: Number(point.value) }));
}

/** Funding settlements as percent of the position, a rate of zero or above marked positive. */
export function fundingBars(points: readonly FundingInput[]): FundingBar[] {
  return points.map((point) => ({
    time: secondsOf(point.time),
    value: Number(point.rate) * 100,
    positive: Number(point.rate) >= 0,
  }));
}
