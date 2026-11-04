import { candleLegend } from './candleLegend';

const candle = (openTime: string, close: string) => ({
  openTime,
  open: '185.92',
  high: '186.71',
  low: '185.8',
  close,
  volume: '412830',
  turnover: '1',
});

const candles = [candle('2026-11-04T13:30:00Z', '186.1'), candle('2026-11-04T13:45:00Z', '186.42')];

describe('candle legend', () => {
  it('tells the candle under the cursor', () => {
    expect(candleLegend(candles, Date.parse('2026-11-04T13:30:00Z') / 1000)).toEqual({
      time: '04.11 13:30',
      open: '185.92',
      high: '186.71',
      low: '185.8',
      close: '186.1',
      volume: '412 830',
    });
  });

  it('tells the last closed candle without a cursor', () => {
    expect(candleLegend(candles, null)?.close).toBe('186.42');
  });

  it('tells the last closed candle when the cursor is where no candle is', () => {
    expect(candleLegend(candles, Date.parse('2026-11-04T14:00:00Z') / 1000)?.close).toBe('186.42');
  });

  it('tells nothing without candles', () => {
    expect(candleLegend([], null)).toBeNull();
  });
});
