import {
  feedText,
  type FormingCandle,
  formingCandles,
  formingReducer,
  type FormingState,
  initialForming,
} from './formingCandle';

const candle = (openTime: string, close = '105', isClosed = false): FormingCandle => ({
  openTime,
  open: '100',
  high: '110',
  low: '95',
  close,
  volume: '7',
  isClosed,
});

const ten = candle('2024-03-10T10:00:00Z');
const eleven = candle('2024-03-10T11:00:00Z');

describe('formingReducer', () => {
  it('starts connecting without a candle', () => {
    expect(initialForming).toEqual({ feed: 'connecting', current: null, completed: null });
  });

  it('takes the state and the candle of a snapshot', () => {
    expect(
      formingReducer(initialForming, {
        type: 'snapshot',
        snapshot: { state: 'live', candle: ten },
      }),
    ).toEqual({
      feed: 'live',
      current: ten,
      completed: null,
    });
    expect(
      formingReducer(initialForming, {
        type: 'snapshot',
        snapshot: { state: 'noData', candle: null },
      }),
    ).toEqual({ feed: 'noData', current: null, completed: null });
  });

  it('does not let a snapshot answered after newer messages overwrite them', () => {
    const newer = candle('2024-03-10T10:00:00Z', '108');
    const state = formingReducer(
      formingReducer(initialForming, { type: 'candle', candle: newer }),
      { type: 'status', status: 'noData' },
    );

    expect(
      formingReducer(state, {
        type: 'snapshot',
        snapshot: { state: 'live', candle: ten },
        superseded: true,
      }),
    ).toBe(state);
  });

  it('still takes the candle of a superseded snapshot when no candle has come yet', () => {
    const state = formingReducer(initialForming, { type: 'status', status: 'noData' });

    expect(
      formingReducer(state, {
        type: 'snapshot',
        snapshot: { state: 'live', candle: ten },
        superseded: true,
      }),
    ).toEqual({ feed: 'noData', current: ten, completed: null });
  });

  it('shows a candle as live and replaces the current candle of the same interval', () => {
    const updated = candle('2024-03-10T10:00:00Z', '106');
    const state = formingReducer(formingReducer(initialForming, { type: 'candle', candle: ten }), {
      type: 'candle',
      candle: updated,
    });

    expect(state).toEqual({ feed: 'live', current: updated, completed: null });
  });

  it('keeps the previous candle when the next interval opens', () => {
    const state = formingReducer(formingReducer(initialForming, { type: 'candle', candle: ten }), {
      type: 'candle',
      candle: eleven,
    });

    expect(state).toEqual({ feed: 'live', current: eleven, completed: ten });
  });

  it('ignores a candle older than the current one', () => {
    const state: FormingState = { feed: 'live', current: eleven, completed: ten };

    expect(
      formingReducer(state, { type: 'candle', candle: candle('2024-03-10T09:00:00Z') }),
    ).toEqual(state);
  });

  it('takes the status of the channel and keeps the candles', () => {
    const state: FormingState = { feed: 'live', current: ten, completed: null };

    expect(formingReducer(state, { type: 'status', status: 'noData' })).toEqual({
      ...state,
      feed: 'noData',
    });
  });

  it('shows no data when the connection is lost, with the candles kept', () => {
    const state: FormingState = { feed: 'live', current: ten, completed: null };

    expect(formingReducer(state, { type: 'lost' })).toEqual({ ...state, feed: 'noData' });
  });

  it('starts over on reset', () => {
    const state: FormingState = { feed: 'live', current: ten, completed: null };

    expect(formingReducer(state, { type: 'reset' })).toEqual(initialForming);
  });
});

describe('formingCandles', () => {
  it('lists the previous and the current candle, oldest first', () => {
    expect(formingCandles({ feed: 'live', current: eleven, completed: ten })).toEqual([
      ten,
      eleven,
    ]);
    expect(formingCandles(initialForming)).toEqual([]);
  });
});

describe('feedText', () => {
  it('names the state of the channel', () => {
    expect(feedText('connecting')).toBe('подключение');
    expect(feedText('live')).toBe('в реальном времени');
    expect(feedText('noData')).toBe('нет данных');
  });
});
