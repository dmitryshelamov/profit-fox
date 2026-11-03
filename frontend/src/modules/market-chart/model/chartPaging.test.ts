import {
  candleEnd,
  chartPageCandles,
  intervalSeconds,
  mergeCandles,
  pageBefore,
  tooFarBehind,
} from './chartPaging';

describe('pageBefore', () => {
  it('takes 500 candles before the open time by the length of the interval', () => {
    expect(pageBefore('2024-03-10T12:00:00.000Z', '60')).toEqual({
      from: '2024-02-18T16:00:00.000Z',
      to: '2024-03-10T12:00:00.000Z',
    });
    expect(chartPageCandles).toBe(500);
  });

  it('counts a day as 86400 seconds', () => {
    expect(pageBefore('2024-03-10T00:00:00.000Z', 'D').from).toBe('2022-10-27T00:00:00.000Z');
  });
});

describe('intervalSeconds', () => {
  it.each([
    ['1', 60],
    ['240', 14_400],
    ['D', 86_400],
    ['W', 604_800],
  ])('knows interval %s', (interval, seconds) => {
    expect(intervalSeconds[interval]).toBe(seconds);
  });
});

describe('tooFarBehind', () => {
  const now = new Date('2024-03-10T12:00:00Z');

  it('is false while the candles since the open time fit in one request', () => {
    expect(tooFarBehind('2024-01-28T21:00:00Z', now, '60')).toBe(false);
  });

  it('is true once more than 999 candles passed', () => {
    expect(tooFarBehind('2024-01-28T20:00:00Z', now, '60')).toBe(true);
  });
});

describe('mergeCandles', () => {
  const candle = (openTime: string, close = '1') => ({ openTime, close });

  it('joins the pages by open time, each once, oldest first', () => {
    const merged = mergeCandles([
      { candles: [candle('2024-01-01T02:00:00Z'), candle('2024-01-01T03:00:00Z', '3')] },
      { candles: [candle('2024-01-01T01:00:00Z'), candle('2024-01-01T02:00:00Z', '2')] },
    ]);

    expect(merged).toEqual([
      candle('2024-01-01T01:00:00Z'),
      candle('2024-01-01T02:00:00Z', '2'),
      candle('2024-01-01T03:00:00Z', '3'),
    ]);
  });
});

describe('candleEnd', () => {
  const seconds = (iso: string) => Date.parse(iso) / 1000;

  it('adds the fixed length of an interval below a month', () => {
    expect(candleEnd(seconds('2024-03-10T10:00:00Z'), '60')).toBe(seconds('2024-03-10T11:00:00Z'));
    expect(candleEnd(seconds('2024-03-10T00:00:00Z'), 'W')).toBe(seconds('2024-03-17T00:00:00Z'));
  });

  it.each([
    ['2024-02-01T00:00:00Z', '2024-03-01T00:00:00Z'],
    ['2023-02-01T00:00:00Z', '2023-03-01T00:00:00Z'],
    ['2024-12-01T00:00:00Z', '2025-01-01T00:00:00Z'],
    ['2024-03-01T00:00:00Z', '2024-04-01T00:00:00Z'],
  ])('ends the month candle %s at the start of the next month', (open, end) => {
    expect(candleEnd(seconds(open), 'M')).toBe(seconds(end));
  });
});
