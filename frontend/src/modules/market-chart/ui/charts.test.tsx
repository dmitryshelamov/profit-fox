import { render } from '@testing-library/react';
import { createChart, createSeriesMarkers } from 'lightweight-charts';

import type { BarInput, LiveCandle } from '../model/liveCandles';
import type { OrderInput } from '../model/orderLines';
import type { FillMarker, PricePoint } from '../model/pricePoint';
import type { FillInput } from '../model/tradeMarkers';
import { BotCandleChart } from './BotCandleChart';
import { EquityComparisonChart, type EquityLine } from './EquityComparisonChart';
import { PriceEquityChart } from './PriceEquityChart';

const points: PricePoint[] = [
  {
    openTime: '2024-01-01T00:00:00Z',
    open: '100',
    high: '110',
    low: '95',
    close: '105',
    equity: '1000',
  },
];

const entry = (time: number): FillMarker => ({ time, side: 'buy', kind: 'entry', count: 1 });

const markersPlugin = (index: number) =>
  vi.mocked(createSeriesMarkers).mock.results[index]!.value as {
    setMarkers: ReturnType<typeof vi.fn>;
  };

beforeEach(() => {
  vi.mocked(createChart).mockClear();
  vi.mocked(createSeriesMarkers).mockClear();
});

describe('price and equity chart', () => {
  it('attaches one markers plugin and updates it when the markers change', () => {
    const { rerender } = render(
      <PriceEquityChart points={points} markers={[entry(1)]} label="c" />,
    );

    expect(createSeriesMarkers).toHaveBeenCalledTimes(1);
    expect(markersPlugin(0).setMarkers).toHaveBeenLastCalledWith([
      expect.objectContaining({ time: 1 }),
    ]);

    rerender(<PriceEquityChart points={points} markers={[entry(1), entry(2)]} label="c" />);

    expect(createSeriesMarkers).toHaveBeenCalledTimes(1);
    expect(markersPlugin(0).setMarkers).toHaveBeenLastCalledWith([
      expect.objectContaining({ time: 1 }),
      expect.objectContaining({ time: 2 }),
    ]);
  });
});

describe('equity comparison chart', () => {
  const line = (label: string): EquityLine => ({
    label,
    color: '--color-series-1',
    points: [{ openTime: '2024-01-01T00:00:00Z', equity: '1000' }],
  });
  const chartOf = () =>
    vi.mocked(createChart).mock.results[0]!.value as { removeSeries: ReturnType<typeof vi.fn> };

  it('takes the old lines off the chart when the lines change', () => {
    const { rerender } = render(<EquityComparisonChart lines={[line('a')]} label="c" />);

    rerender(<EquityComparisonChart lines={[line('b')]} label="c" />);

    expect(chartOf().removeSeries).toHaveBeenCalledTimes(1);
  });

  it('leaves the lines to the chart when the page goes away', () => {
    const { unmount } = render(<EquityComparisonChart lines={[line('a')]} label="c" />);

    unmount();

    expect(chartOf().removeSeries).not.toHaveBeenCalled();
  });
});

describe('bot candle chart', () => {
  const hour = 3600;
  const bar = (openTime: string, open: string, close: string, volume = '7'): BarInput => ({
    openTime,
    open,
    high: '120',
    low: '90',
    close,
    volume,
  });
  const history = [
    bar('2024-03-10T10:00:00Z', '100', '110', '5'),
    bar('2024-03-10T11:00:00Z', '110', '100', '8'),
  ];
  const t10 = Date.parse('2024-03-10T10:00:00Z') / 1000;
  const current = (close: string, volume: string): LiveCandle => ({
    ...bar('2024-03-10T12:00:00Z', '100', close, volume),
    isClosed: false,
  });
  const sell: OrderInput = {
    side: 'sell',
    type: 'limit',
    status: 'active',
    price: '105000',
    quantity: '0.01',
    filledQuantity: '0',
  };

  function show(
    props: Partial<Parameters<typeof BotCandleChart>[0]> = {},
  ): ReturnType<typeof render> {
    return render(
      <BotCandleChart
        history={history}
        live={[]}
        fills={[]}
        orders={[]}
        interval="60"
        label="График свечей BTCUSDT"
        onReachStart={() => undefined}
        {...props}
      />,
    );
  }

  const chartApi = () =>
    vi.mocked(createChart).mock.results.at(-1)!.value as {
      addSeries: ReturnType<typeof vi.fn>;
      timeScale: () => Record<string, ReturnType<typeof vi.fn>>;
    };
  const seriesOf = (type: string) => {
    const add = chartApi().addSeries;

    return add.mock.results[add.mock.calls.findIndex(([kind]) => kind === type)]!.value as {
      setData: ReturnType<typeof vi.fn>;
      update: ReturnType<typeof vi.fn>;
      createPriceLine: ReturnType<typeof vi.fn>;
      removePriceLine: ReturnType<typeof vi.fn>;
      applyOptions: ReturnType<typeof vi.fn>;
    };
  };

  it('draws the history as candles and as volume columns coloured by direction', () => {
    show();

    expect(seriesOf('Candlestick').setData).toHaveBeenLastCalledWith([
      { time: t10, open: 100, high: 120, low: 90, close: 110 },
      { time: t10 + hour, open: 110, high: 120, low: 90, close: 100 },
    ]);
    expect(seriesOf('Histogram').setData).toHaveBeenLastCalledWith([
      { time: t10, value: 5, color: 'rgba(63, 179, 127, 0.5)' },
      { time: t10 + hour, value: 8, color: 'rgba(229, 72, 77, 0.5)' },
    ]);
  });

  it('keeps the volume on its own hidden scale in the lower part of the pane', () => {
    show();

    const options = chartApi().addSeries.mock.calls.find(([kind]) => kind === 'Histogram')![1];
    expect(options).toEqual(
      expect.objectContaining({
        priceScaleId: 'volume',
        lastValueVisible: false,
        priceLineVisible: false,
      }),
    );
  });

  it('draws the current candle see-through and its volume with update, not setData', () => {
    const { rerender } = show({ live: [current('105', '2')] });
    const candles = seriesOf('Candlestick');
    const volume = seriesOf('Histogram');

    expect(candles.update).toHaveBeenLastCalledWith({
      time: t10 + 2 * hour,
      open: 100,
      high: 120,
      low: 90,
      close: 105,
      color: 'rgba(63, 179, 127, 0.45)',
      wickColor: 'rgba(63, 179, 127, 0.45)',
      borderColor: 'rgba(63, 179, 127, 0.45)',
    });
    expect(volume.update).toHaveBeenLastCalledWith({
      time: t10 + 2 * hour,
      value: 2,
      color: 'rgba(63, 179, 127, 0.25)',
    });
    const setCalls = candles.setData.mock.calls.length;

    rerender(
      <BotCandleChart
        history={history}
        live={[current('99', '3')]}
        fills={[]}
        orders={[]}
        interval="60"
        label="c"
        onReachStart={() => undefined}
      />,
    );

    expect(candles.setData).toHaveBeenCalledTimes(setCalls);
    expect(candles.update).toHaveBeenLastCalledWith(expect.objectContaining({ close: 99 }));
    expect(volume.update).toHaveBeenLastCalledWith(expect.objectContaining({ value: 3 }));
  });

  it('draws the candle that just closed as an ordinary one', () => {
    show({ live: [{ ...current('105', '2'), isClosed: true }] });

    expect(seriesOf('Candlestick').update).toHaveBeenLastCalledWith({
      time: t10 + 2 * hour,
      open: 100,
      high: 120,
      low: 90,
      close: 105,
    });
  });

  it('does not draw a current candle the history already has', () => {
    show({ live: [{ ...bar('2024-03-10T11:00:00Z', '1', '2'), isClosed: true }] });

    expect(seriesOf('Candlestick').update).not.toHaveBeenCalled();
  });

  it('marks fills with the tip on the price and one label for several of a side', () => {
    const fills: FillInput[] = [
      { at: '2024-03-10T10:10:00Z', side: 'buy', price: '100000' },
      { at: '2024-03-10T10:20:00Z', side: 'buy', price: '99500' },
    ];
    show({ fills });

    const setMarkers = markersPlugin(0).setMarkers;
    expect(setMarkers).toHaveBeenLastCalledWith([
      expect.objectContaining({
        time: t10,
        position: 'atPriceTop',
        shape: 'arrowUp',
        price: 100000,
        text: '',
        color: '#3fb37f',
      }),
      expect.objectContaining({ time: t10, price: 99500, text: '×2' }),
    ]);
  });

  it('shows no fill that no drawn candle covers', () => {
    show({ fills: [{ at: '2024-03-10T09:00:00Z', side: 'buy', price: '1' }] });

    expect(markersPlugin(0).setMarkers).toHaveBeenLastCalledWith([]);
  });

  it('puts a fill of the current candle on it', () => {
    show({
      live: [current('105', '2')],
      fills: [{ at: '2024-03-10T12:30:00Z', side: 'sell', price: '101' }],
    });

    expect(markersPlugin(0).setMarkers).toHaveBeenLastCalledWith([
      expect.objectContaining({
        time: t10 + 2 * hour,
        position: 'atPriceBottom',
        shape: 'arrowDown',
        color: '#e5484d',
      }),
    ]);
  });

  it('draws the active orders as dashed lines and replaces them when the orders change', () => {
    const { rerender } = show({ orders: [sell] });
    const candles = seriesOf('Candlestick');

    expect(candles.createPriceLine).toHaveBeenLastCalledWith(
      expect.objectContaining({ price: 105000, lineStyle: 2, title: 'Продажа 105000 · 0.01' }),
    );

    rerender(
      <BotCandleChart
        history={history}
        live={[]}
        fills={[]}
        orders={[]}
        interval="60"
        label="c"
        onReachStart={() => undefined}
      />,
    );

    expect(candles.removePriceLine).toHaveBeenCalledTimes(1);
  });

  it('describes what it shows with the number of fills and of orders', () => {
    const { getByRole } = show({
      orders: [sell],
      fills: [
        { at: '2024-03-10T10:10:00Z', side: 'buy', price: '1' },
        { at: '2024-03-10T11:10:00Z', side: 'sell', price: '1' },
        { at: '2024-03-10T01:10:00Z', side: 'sell', price: '1' },
      ],
    });

    expect(
      getByRole('img', { name: 'График свечей BTCUSDT, исполнений: 2, ордеров: 1' }),
    ).toBeInTheDocument();
  });

  it('asks for older candles when the view comes near the first one', () => {
    const onReachStart = vi.fn();
    show({ onReachStart });
    const subscribe = chartApi().timeScale().subscribeVisibleLogicalRangeChange!;
    const onRange = subscribe.mock.calls.at(-1)![0] as (
      range: { from: number; to: number } | null,
    ) => void;

    onRange({ from: 40, to: 100 });
    onRange(null);
    expect(onReachStart).not.toHaveBeenCalled();

    onRange({ from: 3, to: 60 });
    expect(onReachStart).toHaveBeenCalledTimes(1);
  });

  it('does not ask for older candles for a view change the chart makes while it takes in new data', () => {
    const onReachStart = vi.fn();
    const { rerender } = show({ onReachStart });
    const onRange = chartApi()
      .timeScale()
      .subscribeVisibleLogicalRangeChange!.mock.calls.at(-1)![0] as (range: {
      from: number;
      to: number;
    }) => void;
    // The library announces the view synchronously from `setData`, before the view is moved back onto the same candles.
    seriesOf('Candlestick').setData.mockImplementation(() => onRange({ from: 0, to: 50 }));

    rerender(
      <BotCandleChart
        history={[bar('2024-03-10T09:00:00Z', '1', '2'), ...history]}
        live={[]}
        fills={[]}
        orders={[]}
        interval="60"
        label="c"
        onReachStart={onReachStart}
      />,
    );

    expect(onReachStart).not.toHaveBeenCalled();

    onRange({ from: 0, to: 50 });
    expect(onReachStart).toHaveBeenCalledTimes(1);
  });

  it('opens on the newest candles and keeps the view when older candles arrive', () => {
    const scale = () => chartApi().timeScale();
    const { rerender } = show();

    expect(scale().setVisibleLogicalRange).toHaveBeenLastCalledWith({ from: 0, to: 4 });

    scale().getVisibleLogicalRange!.mockReturnValue({ from: 1, to: 3 });
    scale().setVisibleLogicalRange!.mockClear();
    rerender(
      <BotCandleChart
        history={[
          bar('2024-03-10T08:00:00Z', '1', '2'),
          bar('2024-03-10T09:00:00Z', '1', '2'),
          ...history,
        ]}
        live={[]}
        fills={[]}
        orders={[]}
        interval="60"
        label="c"
        onReachStart={() => undefined}
      />,
    );

    expect(scale().setVisibleLogicalRange).toHaveBeenLastCalledWith({ from: 3, to: 5 });
  });
});
