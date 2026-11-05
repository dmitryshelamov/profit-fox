import { bandRect, firedMark, levelLines, withLevels, zoneBands } from './ideaOverlay';

describe('levelLines', () => {
  it('turns the levels into price lines with their kind and title', () => {
    expect(
      levelLines([
        { kind: 'entry', price: '2.40', title: 'Вход 2.40' },
        { kind: 'stop', price: '2.3', title: 'Стоп 2.3' },
      ]),
    ).toEqual([
      { kind: 'entry', price: 2.4, title: 'Вход 2.40' },
      { kind: 'stop', price: 2.3, title: 'Стоп 2.3' },
    ]);
  });
});

describe('zoneBands', () => {
  it('turns the zones into bands between two prices', () => {
    expect(zoneBands([{ low: '2.1', high: '2.15', kind: 'support' }])).toEqual([
      { low: 2.1, high: 2.15, kind: 'support' },
    ]);
  });
});

describe('firedMark', () => {
  it('stands on the open of the candle the idea fired in', () => {
    // 11:47:30 on 15 minutes is in the candle of 11:45.
    expect(firedMark('2026-11-04T11:47:30Z', '15')).toBe(Date.parse('2026-11-04T11:45:00Z') / 1000);
    expect(firedMark('2026-11-04T11:47:30Z', '240')).toBe(
      Date.parse('2026-11-04T08:00:00Z') / 1000,
    );
  });
});

describe('bandRect', () => {
  const y = (price: number) => 1000 - price * 10;

  it('spans from the higher price down to the lower one on the screen', () => {
    expect(bandRect({ low: 20, high: 30 }, y)).toEqual({ top: 700, height: 100 });
  });

  it('is empty when the chart cannot place a price', () => {
    expect(bandRect({ low: 20, high: 30 }, (price) => (price === 30 ? null : 5))).toBeNull();
  });
});

describe('withLevels', () => {
  const info = { priceRange: { minValue: 150, maxValue: 170 }, margins: { above: 1, below: 2 } };

  it('widens the range of the candles to hold every level', () => {
    expect(withLevels(info, [160.68, 189, 140])).toEqual({
      priceRange: { minValue: 140, maxValue: 189 },
      margins: { above: 1, below: 2 },
    });
  });

  it('keeps the range when the levels lie inside it', () => {
    expect(withLevels(info, [155, 165])).toEqual(info);
  });

  it('leaves the range alone without levels', () => {
    expect(withLevels(info, [])).toBe(info);
    expect(withLevels(null, [])).toBeNull();
  });

  it('takes the levels as the range when the series has no candles', () => {
    expect(withLevels(null, [2.3, 2.6, 2.4])).toEqual({
      priceRange: { minValue: 2.3, maxValue: 2.6 },
    });
  });
});
