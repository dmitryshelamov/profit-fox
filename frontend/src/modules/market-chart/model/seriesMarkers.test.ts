import { seriesMarkers } from './seriesMarkers';

describe('series markers', () => {
  it('puts entries under the bar and exits above it, and counts several fills', () => {
    expect(
      seriesMarkers([
        { time: 100, side: 'buy', kind: 'entry', count: 1 },
        { time: 200, side: 'sell', kind: 'exit', count: 3 },
      ]),
    ).toEqual([
      { time: 100, position: 'belowBar', shape: 'arrowUp', kind: 'entry', text: '' },
      { time: 200, position: 'aboveBar', shape: 'arrowDown', kind: 'exit', text: '3' },
    ]);
  });

  it('draws a short entry as a sell above the bar', () => {
    expect(seriesMarkers([{ time: 100, side: 'sell', kind: 'entry', count: 1 }])).toEqual([
      { time: 100, position: 'aboveBar', shape: 'arrowDown', kind: 'entry', text: '' },
    ]);
  });
});
