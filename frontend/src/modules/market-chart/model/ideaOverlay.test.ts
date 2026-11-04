import { bandRect, firedMark, levelLines, zoneBands } from './ideaOverlay';

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
