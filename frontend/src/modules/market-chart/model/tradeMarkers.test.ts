import { candleEnd } from './chartPaging';
import { type FillInput, tradeMarkers } from './tradeMarkers';

const hour = 3600;
const t0 = Date.parse('2024-03-10T10:00:00Z') / 1000;
const candles = [0, 1, 2].map((index) => ({ time: t0 + index * hour }));
const ends = (time: number) => time + hour;
const fill = (at: string, side: 'buy' | 'sell', price: string): FillInput => ({ at, side, price });

describe('tradeMarkers', () => {
  it('puts a buy on the candle that contains it with the tip up at the price', () => {
    expect(tradeMarkers(candles, [fill('2024-03-10T11:30:00Z', 'buy', '100000')], ends)).toEqual([
      { time: t0 + hour, position: 'atPriceTop', shape: 'arrowUp', price: 100000, text: '' },
    ]);
  });

  it('puts a sell with the tip down at the price', () => {
    expect(tradeMarkers(candles, [fill('2024-03-10T10:00:00Z', 'sell', '105000.5')], ends)).toEqual(
      [{ time: t0, position: 'atPriceBottom', shape: 'arrowDown', price: 105000.5, text: '' }],
    );
  });

  it('gives fills of one side on one candle one label with their number', () => {
    const markers = tradeMarkers(
      candles,
      [
        fill('2024-03-10T10:10:00Z', 'buy', '100000'),
        fill('2024-03-10T10:20:00Z', 'buy', '99500'),
        fill('2024-03-10T10:30:00Z', 'sell', '101000'),
      ],
      ends,
    );

    expect(markers.map((marker) => [marker.shape, marker.price, marker.text])).toEqual([
      ['arrowUp', 100000, ''],
      ['arrowUp', 99500, '×2'],
      ['arrowDown', 101000, ''],
    ]);
  });

  it('orders the markers by time', () => {
    const markers = tradeMarkers(
      candles,
      [fill('2024-03-10T12:10:00Z', 'buy', '1'), fill('2024-03-10T10:10:00Z', 'buy', '2')],
      ends,
    );

    expect(markers.map((marker) => marker.time)).toEqual([t0, t0 + 2 * hour]);
  });

  it('leaves out a fill before the first candle and one after the last candle ends', () => {
    expect(
      tradeMarkers(
        candles,
        [fill('2024-03-10T09:59:59Z', 'buy', '1'), fill('2024-03-10T13:00:00Z', 'buy', '1')],
        ends,
      ),
    ).toEqual([]);
  });

  it('leaves out a fill in a gap between candles', () => {
    const gapped = [{ time: t0 }, { time: t0 + 3 * hour }];

    expect(tradeMarkers(gapped, [fill('2024-03-10T12:00:00Z', 'buy', '1')], ends)).toEqual([]);
  });

  it('keeps a fill of 1 March off the February candle when candles are months', () => {
    const february = Date.parse('2024-02-01T00:00:00Z') / 1000;
    const monthEnd = (time: number) => candleEnd(time, 'M');

    expect(
      tradeMarkers([{ time: february }], [fill('2024-03-01T12:00:00Z', 'buy', '1')], monthEnd),
    ).toEqual([]);
    expect(
      tradeMarkers([{ time: february }], [fill('2024-02-29T23:00:00Z', 'buy', '1')], monthEnd),
    ).toHaveLength(1);
  });
});
