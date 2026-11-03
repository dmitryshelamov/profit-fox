import { act, renderHook } from '@testing-library/react';

import { followFormingCandle, type FormingHandlers } from '../realtime/followFormingCandle';
import type { FormingCandle } from './formingCandle';
import { useFormingCandle } from './useFormingCandle';

vi.mock('../realtime/followFormingCandle', () => ({ followFormingCandle: vi.fn() }));

const candle: FormingCandle = {
  openTime: '2024-03-10T10:00:00Z',
  open: '100',
  high: '110',
  low: '95',
  close: '105',
  volume: '7',
  isClosed: false,
};

describe('useFormingCandle', () => {
  let handlers: FormingHandlers;
  const stop = vi.fn();

  beforeEach(() => {
    stop.mockReset();
    vi.mocked(followFormingCandle).mockImplementation((_botId, next) => {
      handlers = next;

      return stop;
    });
  });

  it('follows the bot and shows its candle and the state of the channel', () => {
    const { result } = renderHook(() => useFormingCandle('bot-1'));

    expect(followFormingCandle).toHaveBeenCalledWith('bot-1', expect.any(Object));
    expect(result.current).toEqual({ feed: 'connecting', live: [] });

    act(() => handlers.onSnapshot({ state: 'live', candle }, false));
    expect(result.current).toEqual({ feed: 'live', live: [candle] });

    act(() => handlers.onStatus('noData'));
    expect(result.current.feed).toBe('noData');

    act(() => handlers.onCandle({ ...candle, close: '106' }));
    expect(result.current).toEqual({ feed: 'live', live: [{ ...candle, close: '106' }] });

    act(() => handlers.onLost());
    expect(result.current.feed).toBe('noData');
    expect(result.current.live).toHaveLength(1);
  });

  it('stops following when the page goes away and starts over for another bot', () => {
    const { result, rerender, unmount } = renderHook(({ id }) => useFormingCandle(id), {
      initialProps: { id: 'bot-1' },
    });
    act(() => handlers.onCandle(candle));

    rerender({ id: 'bot-2' });

    expect(stop).toHaveBeenCalledTimes(1);
    expect(result.current).toEqual({ feed: 'connecting', live: [] });

    unmount();
    expect(stop).toHaveBeenCalledTimes(2);
  });
});
