import { type BarInput, drawnBars, type LiveCandle, liveCandles } from './liveCandles';

const candle = (openTime: string, close = '105', volume = '7'): BarInput => ({
  openTime,
  open: '100',
  high: '110',
  low: '95',
  close,
  volume,
});

const live = (openTime: string, isClosed = false, close = '105'): LiveCandle => ({
  ...candle(openTime, close),
  isClosed,
});

describe('liveCandles', () => {
  const history = [candle('2024-03-10T09:00:00Z'), candle('2024-03-10T10:00:00Z')];

  it('puts the current candle after the history', () => {
    expect(liveCandles(history, [live('2024-03-10T11:00:00Z')])).toEqual([
      live('2024-03-10T11:00:00Z'),
    ]);
  });

  it('does not draw a candle that opened not later than the last candle of the history', () => {
    expect(
      liveCandles(history, [live('2024-03-10T10:00:00Z', true), live('2024-03-10T11:00:00Z')]),
    ).toEqual([live('2024-03-10T11:00:00Z')]);
  });

  it('keeps the previous candle until the history brings it', () => {
    const both = [live('2024-03-10T11:00:00Z', true), live('2024-03-10T12:00:00Z')];

    expect(liveCandles(history, both)).toEqual(both);
    expect(liveCandles([...history, candle('2024-03-10T11:00:00Z')], both)).toEqual([both[1]]);
  });

  it('draws every candle when the history is empty', () => {
    expect(liveCandles([], [live('2024-03-10T11:00:00Z')])).toHaveLength(1);
  });
});

describe('drawnBars', () => {
  it('turns the decimal strings into numbers for the chart and marks the forming ones', () => {
    expect(drawnBars([candle('2024-03-10T09:00:00Z', '105.5', '7.25')], false)).toEqual([
      {
        time: 1_710_061_200,
        open: 100,
        high: 110,
        low: 95,
        close: 105.5,
        volume: 7.25,
        forming: false,
      },
    ]);
    expect(drawnBars([candle('2024-03-10T09:00:00Z')], true)[0]?.forming).toBe(true);
  });
});
