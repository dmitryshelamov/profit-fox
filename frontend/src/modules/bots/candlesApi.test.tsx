import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';

import { fakeApi, json } from '@/test/fakeApi';
import { botId } from '@/test/fixtures';

import { useBotCandles } from './api';

const hour = 3_600_000;
const start = Date.parse('2026-11-04T12:30:00Z');

const candle = (openTime: string, close = '105') => ({
  openTime,
  open: '100',
  high: '110',
  low: '95',
  close,
  volume: '7',
  turnover: '700',
});

/** The query of a request by lower-case names: the API binds them without regard to case. */
const queryOf = (request: Request) =>
  new Map(
    [...new URL(request.url).searchParams].map(([name, value]) => [name.toLowerCase(), value]),
  );

const at = (ms: number) => new Date(ms).toISOString();

function setup(
  answer: (from: number, to: number) => unknown[],
  unavailable: boolean | (() => boolean) = false,
  interval = '60',
) {
  const requests = fakeApi({
    [`GET /api/bots/${botId}/candles`]: (request) => {
      const query = queryOf(request);

      return json({
        interval: query.get('interval'),
        candles: answer(Date.parse(query.get('from')!), Date.parse(query.get('to')!)),
        unavailable: typeof unavailable === 'function' ? unavailable() : unavailable,
      });
    },
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );

  return { requests, ...renderHook(() => useBotCandles(botId, interval), { wrapper }) };
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(start);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('bot candles', () => {
  it('reads 500 candles back from now on the interval', async () => {
    const { result, requests } = setup((_from, to) => [candle(at(to - 2 * hour))]);

    await waitFor(() => expect(result.current.candles).toHaveLength(1));

    const query = queryOf(requests[0]!);
    expect(query.get('interval')).toBe('60');
    expect(query.get('to')).toBe(at(start));
    expect(query.get('from')).toBe(at(start - 500 * hour));
  });

  it('loads the 500 candles before the earliest one and joins them without repeats', async () => {
    const { result, requests } = setup((from) => [candle(at(from)), candle(at(from + hour))]);
    await waitFor(() => expect(result.current.candles).toHaveLength(2));

    act(() => result.current.loadEarlier());

    await waitFor(() => expect(result.current.candles).toHaveLength(4));
    expect(queryOf(requests[1]!).get('to')).toBe(at(start - 500 * hour));
    expect(queryOf(requests[1]!).get('from')).toBe(at(start - 1000 * hour));
    expect(result.current.candles.map((item) => item.openTime)).toEqual(
      [...result.current.candles.map((item) => item.openTime)].sort(),
    );
  });

  it('does not load further back after a page without candles', async () => {
    const { result, requests } = setup(() => []);
    await waitFor(() => expect(result.current.isPending).toBe(false));

    expect(result.current.hasEarlier).toBe(false);
    act(() => result.current.loadEarlier());

    expect(requests).toHaveLength(1);
  });

  it('says that the history is unavailable when a page says so', async () => {
    const { result } = setup(() => [], true);

    await waitFor(() => expect(result.current.unavailable).toBe(true));
  });

  it('brings the candles closed since the first page every minute', async () => {
    let closed = false;
    const { result, requests } = setup((_from, to) =>
      closed ? [candle(at(start - hour), '1'), candle(at(start), '2')] : [candle(at(to - hour))],
    );
    await waitFor(() => expect(result.current.candles).toHaveLength(1));
    closed = true;

    await act(() => vi.advanceTimersByTimeAsync(60_000));

    await waitFor(() => expect(result.current.candles).toHaveLength(2));
    const tail = queryOf(requests.at(-1)!);
    expect(tail.get('from')).toBe(at(start));
    expect(Date.parse(tail.get('to')!)).toBeGreaterThanOrEqual(start + 60_000);
  });

  it('starts over from now when the candles since the first page no longer fit in one request', async () => {
    let step = 0;
    const { result, requests } = setup((_from, to) => [candle(at(to - hour), String(++step))]);
    await waitFor(() => expect(result.current.candles).toHaveLength(1));
    const before = requests.length;

    vi.setSystemTime(start + 1000 * hour);
    await act(() => vi.advanceTimersByTimeAsync(60_000));

    await waitFor(() =>
      expect(
        requests
          .slice(before)
          .some((request) => Date.parse(queryOf(request).get('from')!) >= start + 500 * hour),
      ).toBe(true),
    );
    await waitFor(() => expect(result.current.candles).toHaveLength(1));
  });

  it('reads a first page again after Bybit recovers and goes on paging back', async () => {
    let down = true;
    const { result } = setup(
      (_from, to) => (down ? [] : [candle(at(to - hour))]),
      () => down,
    );
    await waitFor(() => expect(result.current.unavailable).toBe(true));
    expect(result.current.hasEarlier).toBe(false);
    down = false;

    await act(() => vi.advanceTimersByTimeAsync(60_000));

    await waitFor(() => expect(result.current.candles).toHaveLength(1));
    expect(result.current.unavailable).toBe(false);
    expect(result.current.hasEarlier).toBe(true);
  });

  it('reads an older page again that stopped the paging during an outage', async () => {
    let down = false;
    const { result } = setup(
      (from, to) => (to > start - 500 * hour || !down ? [candle(at(from))] : []),
      () => down,
    );
    await waitFor(() => expect(result.current.candles).toHaveLength(1));
    down = true;
    act(() => result.current.loadEarlier());
    await waitFor(() => expect(result.current.unavailable).toBe(true));
    expect(result.current.hasEarlier).toBe(false);
    down = false;

    await act(() => vi.advanceTimersByTimeAsync(60_000));

    await waitFor(() => expect(result.current.unavailable).toBe(false));
    expect(result.current.candles).toHaveLength(2);
    expect(result.current.hasEarlier).toBe(true);
  });

  describe('around a candle close', () => {
    const closeMargin = 5000;
    const close = Date.parse('2026-11-04T13:00:00Z');
    const opens = [-3, -2, -1, 0].map((n) => close + n * hour);
    /** What the server has stored: a closed candle only five seconds after its close. */
    const stored = (from: number, to: number) =>
      opens
        .filter((open) => open >= from && open < to && open + hour + closeMargin <= Date.now())
        .map((open) => candle(at(open)));

    it('brings a candle that was not stored yet when the page was opened', async () => {
      vi.setSystemTime(close + 3000);
      const { result } = setup(stored);
      await waitFor(() => expect(result.current.candles).toHaveLength(2));

      await act(() => vi.advanceTimersByTimeAsync(60_000));

      await waitFor(() => expect(result.current.candles).toHaveLength(3));
    });

    it('reads again soon after a minute read that fell in the first seconds after a close', async () => {
      vi.setSystemTime(close - 57_000);
      const { result } = setup(stored);
      await waitFor(() => expect(result.current.candles).toHaveLength(2));

      await act(() => vi.advanceTimersByTimeAsync(60_000));
      expect(result.current.candles).toHaveLength(2);
      await act(() => vi.advanceTimersByTimeAsync(3000));

      await waitFor(() => expect(result.current.candles).toHaveLength(3));
    });

    it('makes no extra read between a close and the next one', async () => {
      vi.setSystemTime(close + 20_000);
      const { requests } = setup(stored);
      await waitFor(() => expect(requests).toHaveLength(1));

      await act(() => vi.advanceTimersByTimeAsync(59_000));
      expect(requests).toHaveLength(1);
      await act(() => vi.advanceTimersByTimeAsync(2000));

      await waitFor(() => expect(requests).toHaveLength(2));
    });

    it('starts over when the tail, which begins at the newest candle, no longer fits in one request', async () => {
      // The newest candle is 499 hours back (a halt): the tail covers 1000 candles long before 999 hours pass.
      const { result, requests } = setup((_from, to) => [candle(at(to - 499 * hour))]);
      await waitFor(() => expect(result.current.candles).toHaveLength(1));
      const before = requests.length;

      vi.setSystemTime(start + 600 * hour);
      await act(() => vi.advanceTimersByTimeAsync(60_000));

      await waitFor(() =>
        expect(
          requests
            .slice(before)
            .some((request) => Date.parse(queryOf(request).get('to')!) >= start + 600 * hour),
        ).toBe(true),
      );
      await waitFor(() =>
        expect(
          requests
            .slice(before)
            .some((request) => Date.parse(queryOf(request).get('from')!) >= start + 100 * hour),
        ).toBe(true),
      );
    });

    it.each([
      ['W', 604_800],
      ['M', 2_678_400],
    ])(
      'reads the tail of a %s chart once a minute, not at the epoch-aligned close',
      async (interval, seconds) => {
        vi.setSystemTime(seconds * 1000 * 1000 - 20_000);
        const { requests } = setup(() => [], false, interval);
        await waitFor(() => expect(requests).toHaveLength(1));

        await act(() => vi.advanceTimersByTimeAsync(30_000));
        expect(requests).toHaveLength(1);
        await act(() => vi.advanceTimersByTimeAsync(31_000));

        await waitFor(() => expect(requests).toHaveLength(2));
      },
    );
  });
});
