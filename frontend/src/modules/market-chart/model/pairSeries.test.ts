import { fundingBars, openInterestLine } from './pairSeries';

describe('open interest line', () => {
  it('draws each point at its UTC second with the value as a number', () => {
    expect(
      openInterestLine([
        { time: '2026-11-04T10:00:00Z', value: '1200.5' },
        { time: '2026-11-04T10:05:00Z', value: '1190' },
      ]),
    ).toEqual([
      { time: Date.parse('2026-11-04T10:00:00Z') / 1000, value: 1200.5 },
      { time: Date.parse('2026-11-04T10:05:00Z') / 1000, value: 1190 },
    ]);
  });
});

describe('funding bars', () => {
  it('draws each settlement as percent, marking those above zero', () => {
    expect(
      fundingBars([
        { time: '2026-11-04T08:00:00Z', rate: '0.0001' },
        { time: '2026-11-04T16:00:00Z', rate: '-0.00005' },
        { time: '2026-11-05T00:00:00Z', rate: '0' },
      ]),
    ).toEqual([
      { time: Date.parse('2026-11-04T08:00:00Z') / 1000, value: 0.01, positive: true },
      { time: Date.parse('2026-11-04T16:00:00Z') / 1000, value: -0.005, positive: false },
      { time: Date.parse('2026-11-05T00:00:00Z') / 1000, value: 0, positive: true },
    ]);
  });
});
