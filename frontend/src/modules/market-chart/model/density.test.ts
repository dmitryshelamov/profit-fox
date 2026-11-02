import { candlesPerPoint, candlesText, perPointText, pointsText } from './density';

const hours = (...offsets: number[]) =>
  offsets.map((offset) => ({
    openTime: new Date(Date.UTC(2024, 0, 1) + offset * 3_600_000).toISOString(),
  }));

describe('candles per chart point', () => {
  it('is one when every candle is a point', () => {
    expect(candlesPerPoint(hours(0, 1, 2), '60')).toBe(1);
  });

  it('counts the candles between the opens of neighbouring points', () => {
    expect(candlesPerPoint(hours(0, 106, 212), '60')).toBe(106);
  });

  it('is not misled by a gap in the candles: the shortest distance counts', () => {
    expect(candlesPerPoint(hours(0, 106, 211), '60')).toBe(105);
  });

  it('is one for a single point', () => {
    expect(candlesPerPoint(hours(0), '60')).toBe(1);
  });

  it('counts days and weeks', () => {
    expect(candlesPerPoint(hours(0, 72), 'D')).toBe(3);
    expect(candlesPerPoint(hours(0, 24 * 14), 'W')).toBe(2);
  });

  it('counts calendar months', () => {
    expect(
      candlesPerPoint(
        [{ openTime: '2023-11-01T00:00:00Z' }, { openTime: '2024-02-01T00:00:00Z' }],
        'M',
      ),
    ).toBe(3);
  });
});

describe('text of the density', () => {
  it.each([
    [1, 'по 1 свече в точке'],
    [2, 'по 2 свечи в точке'],
    [21, 'по 21 свече в точке'],
    [106, 'по 106 свечей в точке'],
    [12, 'по 12 свечей в точке'],
  ])('%i candles', (count, text) => {
    expect(perPointText(count)).toBe(text);
  });
});

describe('text of a count of candles', () => {
  it.each([
    [1, '1 свеча'],
    [3, '3 свечи'],
    [11, '11 свечей'],
    [96, '96 свечей'],
  ])('%i candles', (count, text) => {
    expect(candlesText(count)).toBe(text);
  });
});

describe('text of a count of points', () => {
  it.each([
    [1, '1 точка'],
    [3, '3 точки'],
    [12, '12 точек'],
    [5000, '5\u00a0000 точек'],
  ])('%i points', (count, text) => {
    expect(pointsText(count)).toBe(text);
  });
});
