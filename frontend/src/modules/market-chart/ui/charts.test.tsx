import { render } from '@testing-library/react';
import { createChart, createSeriesMarkers, TickMarkType } from 'lightweight-charts';
import type { ReactElement } from 'react';

import type { BarInput, LiveCandle } from '../model/liveCandles';
import type { OrderInput } from '../model/orderLines';
import type { FillMarker, PricePoint } from '../model/pricePoint';
import type { FillInput } from '../model/tradeMarkers';
import { BotCandleChart } from './BotCandleChart';
import { EquityComparisonChart, type EquityLine } from './EquityComparisonChart';
import { FundingChart } from './FundingChart';
import { OpenInterestChart } from './OpenInterestChart';
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

  it('draws the levels of an idea as solid lines and replaces them when the idea changes', () => {
    const { rerender } = show({
      levels: [
        { kind: 'entry', price: '2.4', title: 'Вход 2.4' },
        { kind: 'stop', price: '2.3', title: 'Стоп 2.3' },
        { kind: 'target', price: '2.6', title: 'Цель 2.6' },
      ],
    });
    const candles = seriesOf('Candlestick');

    expect(candles.createPriceLine.mock.calls.map(([line]) => line)).toEqual([
      expect.objectContaining({ price: 2.4, color: '#7aa7ff', lineStyle: 0, title: 'Вход 2.4' }),
      expect.objectContaining({ price: 2.3, color: '#e5484d', title: 'Стоп 2.3' }),
      expect.objectContaining({ price: 2.6, color: '#3fb37f', title: 'Цель 2.6' }),
    ]);

    rerender(
      <BotCandleChart
        history={history}
        interval="60"
        label="c"
        levels={[]}
        onReachStart={() => undefined}
      />,
    );

    expect(candles.removePriceLine).toHaveBeenCalledTimes(3);
  });

  it('widens the autoscale of the candles to the levels of the idea and drops them with the idea', () => {
    const { rerender } = show({
      levels: [
        { kind: 'entry', price: '160.68', title: 'Вход' },
        { kind: 'stop', price: '150', title: 'Стоп' },
        { kind: 'target', price: '189', title: 'Цель' },
      ],
    });
    const candles = seriesOf('Candlestick');
    const provider = () =>
      candles.applyOptions.mock.calls.at(-1)![0].autoscaleInfoProvider as (
        base: () => unknown,
      ) => unknown;
    const base = () => ({ priceRange: { minValue: 90, maxValue: 120 } });

    expect(provider()(base)).toEqual({ priceRange: { minValue: 90, maxValue: 189 } });
    expect(chartApi().addSeries.mock.calls[0]![1].autoscaleInfoProvider).toBe(provider());

    rerender(
      <BotCandleChart
        history={history}
        interval="60"
        label="c"
        levels={[]}
        onReachStart={() => undefined}
      />,
    );

    expect(provider()(base)).toEqual(base());
  });

  it('marks the candle an idea fired in', () => {
    show({ signalTime: t10 });

    expect(markersPlugin(0).setMarkers).toHaveBeenLastCalledWith([
      expect.objectContaining({ time: t10, shape: 'circle', text: 'Сигнал' }),
    ]);
  });

  describe('zones', () => {
    type Primitive = {
      attached: (param: unknown) => void;
      paneViews: () => {
        renderer: () => { draw: (target: unknown) => void };
      }[];
    };

    /** Draws the primitive the chart attached on a fake canvas and returns the rectangles it filled. */
    function bandsDrawn() {
      const candles = seriesOf('Candlestick') as unknown as {
        attachPrimitive: ReturnType<typeof vi.fn>;
        priceToCoordinate: ReturnType<typeof vi.fn>;
      };
      candles.priceToCoordinate.mockImplementation((price: number) => 1000 - price * 10);
      const primitive = candles.attachPrimitive.mock.calls[0]![0] as Primitive;
      // The library hands the series to the primitive when it attaches it; the inert chart does not.
      primitive.attached({ series: candles, requestUpdate: () => undefined });
      const fills: { colour: string; rect: number[] }[] = [];
      const context = {
        fillStyle: '',
        fillRect: (...rect: number[]) => fills.push({ colour: context.fillStyle, rect }),
      };
      primitive
        .paneViews()[0]!
        .renderer()
        .draw({
          useBitmapCoordinateSpace: (draw: (scope: unknown) => void) =>
            draw({ context, bitmapSize: { width: 800, height: 400 }, verticalPixelRatio: 2 }),
        });

      return fills;
    }

    it('draws each zone as a band across the pane, support green and resistance red', () => {
      show({
        zones: [
          { low: '20', high: '30', kind: 'support' },
          { low: '50', high: '55', kind: 'resistance' },
        ],
      });

      expect(bandsDrawn()).toEqual([
        { colour: 'rgba(63, 179, 127, 0.16)', rect: [0, 1400, 800, 200] },
        { colour: 'rgba(229, 72, 77, 0.16)', rect: [0, 900, 800, 100] },
      ]);
    });

    it('draws nothing without zones', () => {
      show();

      expect(bandsDrawn()).toEqual([]);
    });
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

  it('describes a chart without fills and orders, as the chart of a pair, by its label only', () => {
    const { getByRole } = render(
      <BotCandleChart
        history={history}
        interval="60"
        label="График свечей SOLUSDT"
        onReachStart={() => undefined}
      />,
    );

    expect(getByRole('img', { name: 'График свечей SOLUSDT' })).toBeInTheDocument();
    expect(seriesOf('Candlestick').setData).toHaveBeenLastCalledWith([
      expect.objectContaining({ time: t10 }),
      expect.objectContaining({ time: t10 + hour }),
    ]);
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

describe('open interest and funding charts', () => {
  const t0 = Date.parse('2026-11-04T08:00:00Z') / 1000;
  const lastSeries = () => {
    const add = vi.mocked(createChart).mock.results.at(-1)!.value.addSeries as ReturnType<
      typeof vi.fn
    >;

    return add.mock.results[0]!.value as { setData: ReturnType<typeof vi.fn> };
  };
  const fitContent = () =>
    vi.mocked(createChart).mock.results.at(-1)!.value.timeScale().fitContent as ReturnType<
      typeof vi.fn
    >;

  it('draws the open interest as a line over the whole period', () => {
    const { getByRole } = render(
      <OpenInterestChart
        formatValue={String}
        points={[
          { time: '2026-11-04T08:00:00Z', value: '1200.5' },
          { time: '2026-11-04T08:05:00Z', value: '1190' },
        ]}
        label="Открытый интерес SOLUSDT"
      />,
    );

    expect(vi.mocked(createChart).mock.results.at(-1)!.value.addSeries).toHaveBeenCalledWith(
      'Line',
      expect.anything(),
    );
    expect(lastSeries().setData).toHaveBeenLastCalledWith([
      { time: t0, value: 1200.5 },
      { time: t0 + 300, value: 1190 },
    ]);
    expect(fitContent()).toHaveBeenCalled();
    expect(getByRole('img', { name: 'Открытый интерес SOLUSDT' })).toBeInTheDocument();
  });

  it('draws funding as columns, green above zero and red below', () => {
    render(
      <FundingChart
        points={[
          { time: '2026-11-04T08:00:00Z', rate: '0.0001' },
          { time: '2026-11-04T16:00:00Z', rate: '-0.00005' },
        ]}
        label="Фандинг SOLUSDT"
      />,
    );

    expect(vi.mocked(createChart).mock.results.at(-1)!.value.addSeries).toHaveBeenCalledWith(
      'Histogram',
      expect.anything(),
    );
    expect(lastSeries().setData).toHaveBeenLastCalledWith([
      { time: t0, value: 0.01, color: '#3fb37f' },
      { time: t0 + 8 * 3600, value: -0.005, color: '#e5484d' },
    ]);
    expect(fitContent()).toHaveBeenCalled();
  });

  it('draws the new points when they change', () => {
    const { rerender } = render(<OpenInterestChart points={[]} label="c" formatValue={String} />);

    rerender(
      <OpenInterestChart
        points={[{ time: '2026-11-04T08:00:00Z', value: '5' }]}
        label="c"
        formatValue={String}
      />,
    );

    expect(lastSeries().setData).toHaveBeenLastCalledWith([{ time: t0, value: 5 }]);
  });
});

// Requirement "Время в местном часовом поясе": every chart labels its axis and crosshair in the local zone (Moscow).
describe('time of the charts', () => {
  const at = Date.parse('2024-01-05T14:00:00Z') / 1000;
  const charts: [string, ReactElement][] = [
    ['price and equity', <PriceEquityChart points={points} markers={[]} label="c" />],
    ['equity comparison', <EquityComparisonChart lines={[]} label="c" />],
    [
      'bot candles',
      <BotCandleChart
        history={[]}
        live={[]}
        fills={[]}
        orders={[]}
        interval="60"
        label="c"
        onReachStart={() => undefined}
      />,
    ],
    ['open interest', <OpenInterestChart points={[]} label="c" formatValue={String} />],
    ['funding', <FundingChart points={[]} label="c" />],
  ];

  it.each(charts)('labels the time of the %s chart in the local zone', (_, chart) => {
    render(chart);

    const options = vi.mocked(createChart).mock.calls.at(-1)![1] as {
      localization: { timeFormatter: (time: number) => string };
      timeScale: { tickMarkFormatter: (time: number, type: TickMarkType) => string };
    };
    expect(options.localization.timeFormatter(at)).toBe('2024-01-05 17:00');
    expect(options.timeScale.tickMarkFormatter(at, TickMarkType.Time)).toBe('17:00');
  });

  it('keeps the price formatter of the open interest chart', () => {
    render(<OpenInterestChart points={[]} label="c" formatValue={() => 'v'} />);

    const options = vi.mocked(createChart).mock.calls.at(-1)![1] as {
      localization: { priceFormatter: (value: number) => string };
    };
    expect(options.localization.priceFormatter(1)).toBe('v');
  });
});
