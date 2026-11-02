import { drawable, drawableEquity, markersOf, type PricePoint } from './pricePoint';

const point = (openTime: string, close = '100'): PricePoint => ({
  openTime,
  open: '99.5',
  high: '101.25',
  low: '98',
  close,
  equity: '10000.5',
});

const sec = (iso: string) => Date.parse(iso) / 1000;

describe('drawable', () => {
  it('turns decimal strings into numbers and the time into UTC seconds', () => {
    expect(drawable([point('2024-01-05T14:00:00Z', '100.1')])).toEqual({
      candles: [
        { time: sec('2024-01-05T14:00:00Z'), open: 99.5, high: 101.25, low: 98, close: 100.1 },
      ],
      equity: [{ time: sec('2024-01-05T14:00:00Z'), value: 10000.5 }],
    });
  });

  it('drops a repeated or out-of-order time', () => {
    const { candles, equity } = drawable([
      point('2024-01-05T14:00:00Z'),
      point('2024-01-05T14:00:00Z'),
      point('2024-01-05T13:00:00Z'),
      point('2024-01-05T15:00:00Z'),
    ]);

    expect(candles.map((candle) => candle.time)).toEqual([
      sec('2024-01-05T14:00:00Z'),
      sec('2024-01-05T15:00:00Z'),
    ]);
    expect(equity).toHaveLength(2);
  });

  it('draws nothing without points', () => {
    expect(drawable([])).toEqual({ candles: [], equity: [] });
  });
});

describe('markers of fills', () => {
  const points = [
    point('2024-01-05T13:00:00Z'),
    point('2024-01-05T14:00:00Z'),
    point('2024-01-05T15:00:00Z'),
  ];

  it('marks an entry and an exit of a long strategy', () => {
    const markers = markersOf(
      [point('2024-01-05T14:00:00Z'), point('2024-01-11T09:00:00Z')],
      [
        { at: '2024-01-05T14:00:00Z', side: 'buy' },
        { at: '2024-01-11T09:00:00Z', side: 'sell' },
      ],
      'long',
    );

    expect(markers).toEqual([
      { time: sec('2024-01-05T14:00:00Z'), side: 'buy', kind: 'entry', count: 1 },
      { time: sec('2024-01-11T09:00:00Z'), side: 'sell', kind: 'exit', count: 1 },
    ]);
  });

  it('reads the sides the other way for a short strategy', () => {
    const markers = markersOf(
      points,
      [
        { at: '2024-01-05T13:00:00Z', side: 'sell' },
        { at: '2024-01-05T15:00:00Z', side: 'buy' },
      ],
      'short',
    );

    expect(markers.map((marker) => marker.kind)).toEqual(['entry', 'exit']);
  });

  it('puts a fill inside a group on the point that opens it', () => {
    const markers = markersOf(points, [{ at: '2024-01-05T14:59:59Z', side: 'buy' }], 'long');

    expect(markers).toEqual([
      { time: sec('2024-01-05T14:00:00Z'), side: 'buy', kind: 'entry', count: 1 },
    ]);
  });

  it('puts a fill exactly at a group boundary on the later group', () => {
    const markers = markersOf(points, [{ at: '2024-01-05T15:00:00Z', side: 'sell' }], 'long');

    expect(markers.map((marker) => marker.time)).toEqual([sec('2024-01-05T15:00:00Z')]);
  });

  it('counts several fills of one side in one point as one marker', () => {
    const markers = markersOf(
      points,
      [
        { at: '2024-01-05T14:10:00Z', side: 'buy' },
        { at: '2024-01-05T14:20:00Z', side: 'buy' },
        { at: '2024-01-05T14:30:00Z', side: 'sell' },
      ],
      'long',
    );

    expect(markers).toEqual([
      { time: sec('2024-01-05T14:00:00Z'), side: 'buy', kind: 'entry', count: 2 },
      { time: sec('2024-01-05T14:00:00Z'), side: 'sell', kind: 'exit', count: 1 },
    ]);
  });

  it('orders markers by time whatever the order of the fills', () => {
    const markers = markersOf(
      points,
      [
        { at: '2024-01-05T15:00:00Z', side: 'sell' },
        { at: '2024-01-05T13:00:00Z', side: 'buy' },
      ],
      'long',
    );

    expect(markers.map((marker) => marker.time)).toEqual([
      sec('2024-01-05T13:00:00Z'),
      sec('2024-01-05T15:00:00Z'),
    ]);
  });

  it('skips a fill before the first point', () => {
    expect(markersOf(points, [{ at: '2024-01-05T12:59:00Z', side: 'buy' }], 'long')).toEqual([]);
  });

  it('puts a marker on a drawn point, not on one that was dropped', () => {
    const markers = markersOf(
      [point('2024-01-05T14:00:00Z'), point('2024-01-05T13:00:00Z')],
      [{ at: '2024-01-05T13:30:00Z', side: 'buy' }],
      'long',
    );

    expect(markers).toEqual([]);
  });
});

describe('drawableEquity', () => {
  it('turns the equity into numbers by UTC seconds and drops a repeated time', () => {
    expect(
      drawableEquity([
        { openTime: '2024-01-05T14:00:00Z', equity: '1000.5' },
        { openTime: '2024-01-05T14:00:00Z', equity: '999' },
        { openTime: '2024-01-05T15:00:00Z', equity: '1010' },
      ]),
    ).toEqual([
      { time: sec('2024-01-05T14:00:00Z'), value: 1000.5 },
      { time: sec('2024-01-05T15:00:00Z'), value: 1010 },
    ]);
  });
});
