import {
  type FlowBar,
  initialLiveFlow,
  liveFlowReducer,
  type LiveMinute,
  panelBars,
} from './tradeFlow';

const bar = (openTime: string, buy = '0', sell = '0'): FlowBar => ({
  openTime,
  buyVolume: '0',
  sellVolume: '0',
  buyTurnover: buy,
  sellTurnover: sell,
  buyTrades: 0,
  sellTrades: 0,
});

const minute = (openTime: string, since: string, buy = '0', sell = '0'): LiveMinute => ({
  ...bar(openTime, buy, sell),
  since,
});

describe('panelBars', () => {
  // Scenario "Страница открыта посреди минуты": the minute the channel joined midway is incomplete, the next one is not.
  it('marks the current bar incomplete while its live minute began before the channel joined', () => {
    const since = '2026-11-11T12:04:30Z';
    const opened = panelBars(
      [bar('2026-11-11T12:03:00Z', '5')],
      '2026-11-11T12:04:00Z',
      [minute('2026-11-11T12:04:00Z', since, '7')],
      '1',
    );
    const next = panelBars(
      [bar('2026-11-11T12:03:00Z', '5')],
      '2026-11-11T12:04:00Z',
      [minute('2026-11-11T12:04:00Z', since, '7'), minute('2026-11-11T12:05:00Z', since, '2')],
      '1',
    );

    expect(opened.bars.map((item) => [item.openTime, item.buyTurnover])).toEqual([
      ['2026-11-11T12:03:00Z', '5'],
      ['2026-11-11T12:04:00.000Z', '7'],
    ]);
    expect(opened).toMatchObject({ current: '2026-11-11T12:04:00.000Z', incomplete: true });
    expect(next.bars.map((item) => item.openTime)).toEqual([
      '2026-11-11T12:03:00Z',
      '2026-11-11T12:04:00.000Z',
      '2026-11-11T12:05:00.000Z',
    ]);
    expect(next).toMatchObject({ current: '2026-11-11T12:05:00.000Z', incomplete: false });
  });

  // Scenario "Часовой интервал": the current bar is its stored minutes and the live minute, incomplete until the next minute.
  it('adds the live minute to the stored minutes of the current hour', () => {
    const since = '2026-11-11T12:40:30Z';
    const during = panelBars(
      [bar('2026-11-11T11:00:00Z', '1'), bar('2026-11-11T12:00:00Z', '100.5', '20')],
      '2026-11-11T12:40:00Z',
      [minute('2026-11-11T12:40:00Z', since, '0.25', '3')],
      '60',
    );
    const after = panelBars(
      [bar('2026-11-11T11:00:00Z', '1'), bar('2026-11-11T12:00:00Z', '100.5', '20')],
      '2026-11-11T12:40:00Z',
      [
        minute('2026-11-11T12:40:00Z', since, '0.25', '3'),
        minute('2026-11-11T12:41:00Z', since, '1'),
      ],
      '60',
    );

    expect(during.bars.at(-1)).toMatchObject({
      openTime: '2026-11-11T12:00:00Z',
      buyTurnover: '100.75',
      sellTurnover: '23',
    });
    expect(during).toMatchObject({ current: '2026-11-11T12:00:00Z', incomplete: true });
    expect(after.bars.at(-1)).toMatchObject({ buyTurnover: '101.75', sellTurnover: '23' });
    expect(after.incomplete).toBe(false);
  });

  // Scenario "Минута закрылась": a minute the closed bars already hold is not added again.
  it('counts a minute once when the closed bars bring it', () => {
    const since = '2026-11-11T12:04:30Z';
    const result = panelBars(
      [bar('2026-11-11T12:00:00Z', '10')],
      '2026-11-11T12:06:00Z',
      [minute('2026-11-11T12:05:00Z', since, '4'), minute('2026-11-11T12:06:00Z', since, '1')],
      '15',
    );

    expect(result.bars).toEqual([{ ...bar('2026-11-11T12:00:00Z', '11') }]);
  });

  it('opens the bar of a new candle for a live minute after the closed bars', () => {
    const result = panelBars(
      [bar('2026-11-11T12:00:00Z', '10')],
      '2026-11-11T12:15:00Z',
      [minute('2026-11-11T12:15:00Z', '2026-11-11T12:14:30Z', '4', '1')],
      '15',
    );

    expect(result.bars.map((item) => [item.openTime, item.buyTurnover, item.sellTurnover])).toEqual(
      [
        ['2026-11-11T12:00:00Z', '10', '0'],
        ['2026-11-11T12:15:00.000Z', '4', '1'],
      ],
    );
    expect(result.current).toBe('2026-11-11T12:15:00.000Z');
  });

  it('has no current bar without a live minute', () => {
    expect(panelBars([bar('2026-11-11T12:00:00Z')], '2026-11-11T12:15:00Z', [], '15')).toEqual({
      bars: [bar('2026-11-11T12:00:00Z')],
      current: null,
      incomplete: false,
    });
  });
});

describe('liveFlowReducer', () => {
  const since = '2026-11-11T12:04:30Z';

  it('keeps the live minute and the one before it, and ignores an older one', () => {
    let state = liveFlowReducer(initialLiveFlow, {
      type: 'minute',
      minute: minute('2026-11-11T12:04:00Z', since, '1'),
    });
    state = liveFlowReducer(state, {
      type: 'minute',
      minute: minute('2026-11-11T12:04:00Z', since, '2'),
    });
    state = liveFlowReducer(state, {
      type: 'minute',
      minute: minute('2026-11-11T12:05:00Z', since, '3'),
    });
    state = liveFlowReducer(state, {
      type: 'minute',
      minute: minute('2026-11-11T12:03:00Z', since, '9'),
    });

    expect(state.feed).toBe('live');
    expect(state.minutes.map((item) => [item.openTime, item.buyTurnover])).toEqual([
      ['2026-11-11T12:04:00Z', '2'],
      ['2026-11-11T12:05:00Z', '3'],
    ]);
  });

  it('takes the state and the sums of the snapshot', () => {
    const state = liveFlowReducer(initialLiveFlow, {
      type: 'snapshot',
      snapshot: { state: 'live', flow: minute('2026-11-11T12:04:00Z', since, '1') },
    });

    expect(state).toEqual({ feed: 'live', minutes: [minute('2026-11-11T12:04:00Z', since, '1')] });
    expect(
      liveFlowReducer(initialLiveFlow, {
        type: 'snapshot',
        snapshot: { state: 'connecting', flow: null },
      }),
    ).toEqual(initialLiveFlow);
  });

  // A message came while the answer of Join was on its way: the answer is older and changes neither the sums nor the state.
  it('keeps the newer sums and state over a snapshot that came late', () => {
    const live = liveFlowReducer(initialLiveFlow, {
      type: 'minute',
      minute: minute('2026-11-11T12:04:00Z', since, '5'),
    });

    const state = liveFlowReducer(live, {
      type: 'snapshot',
      snapshot: { state: 'connecting', flow: minute('2026-11-11T12:04:00Z', since, '1') },
      superseded: true,
    });

    expect(state).toEqual(live);
  });

  it('has no data once the link is lost and tells the state of the channel', () => {
    expect(liveFlowReducer(initialLiveFlow, { type: 'lost' }).feed).toBe('noData');
    expect(liveFlowReducer(initialLiveFlow, { type: 'status', status: 'noData' }).feed).toBe(
      'noData',
    );
    expect(
      liveFlowReducer(
        { feed: 'live', minutes: [minute('2026-11-11T12:04:00Z', since)] },
        { type: 'reset' },
      ),
    ).toEqual(initialLiveFlow);
  });
});
