import type { Bar } from './liveCandles';
import { volumeBars } from './volumeBars';

const bar = (time: number, open: number, close: number, volume: number, forming = false): Bar => ({
  time,
  open,
  high: 120,
  low: 90,
  close,
  volume,
  forming,
});

describe('volumeBars', () => {
  it('takes the volume of each candle and its direction', () => {
    expect(volumeBars([bar(1, 100, 110, 5), bar(2, 110, 100, 7.5), bar(3, 100, 100, 1)])).toEqual([
      { time: 1, value: 5, rising: true, forming: false },
      { time: 2, value: 7.5, rising: false, forming: false },
      { time: 3, value: 1, rising: true, forming: false },
    ]);
  });

  it('marks the bar of the forming candle', () => {
    expect(volumeBars([bar(1, 100, 110, 5, true)])[0]?.forming).toBe(true);
  });
});
