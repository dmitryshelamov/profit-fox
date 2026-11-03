import { HubConnectionBuilder } from '@microsoft/signalr';

import type { FormingCandle, FormingSnapshot } from '../model/formingCandle';
import { followFormingCandle } from './followFormingCandle';

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(),
  LogLevel: { None: 6 },
}));

const candle: FormingCandle = {
  openTime: '2024-03-10T10:00:00Z',
  open: '100',
  high: '110',
  low: '95',
  close: '105',
  volume: '7',
  isClosed: false,
};
const snapshot: FormingSnapshot = { state: 'live', candle };

class FakeConnection {
  readonly listeners = new Map<string, (message: unknown) => void>();
  reconnecting: () => void = () => undefined;
  reconnected: () => void = () => undefined;
  closed: () => void = () => undefined;
  start = vi.fn(() => Promise.resolve());
  stop = vi.fn(() => Promise.resolve());
  invoke = vi.fn<(method: string, botId: string) => Promise<FormingSnapshot>>(() =>
    Promise.resolve(snapshot),
  );
  on = (name: string, listener: (message: unknown) => void) =>
    void this.listeners.set(name, listener);
  onreconnecting = (callback: () => void) => void (this.reconnecting = callback);
  onreconnected = (callback: () => void) => void (this.reconnected = callback);
  onclose = (callback: () => void) => void (this.closed = callback);
}

let connection: FakeConnection;
let builder: {
  withUrl: ReturnType<typeof vi.fn>;
  withAutomaticReconnect: ReturnType<typeof vi.fn>;
  configureLogging: ReturnType<typeof vi.fn>;
  build: ReturnType<typeof vi.fn>;
};

const handlers = () => ({
  onSnapshot: vi.fn(),
  onCandle: vi.fn(),
  onStatus: vi.fn(),
  onLost: vi.fn(),
});

const settle = () => new Promise((resolve) => setTimeout(resolve));

beforeEach(() => {
  connection = new FakeConnection();
  builder = {
    withUrl: vi.fn(() => builder),
    withAutomaticReconnect: vi.fn(() => builder),
    configureLogging: vi.fn(() => builder),
    build: vi.fn(() => connection),
  } as never;
  vi.mocked(HubConnectionBuilder).mockImplementation(function () {
    return builder as never;
  });
});

describe('followFormingCandle', () => {
  it('connects to the hub with reconnection and subscribes to the bot', async () => {
    const on = handlers();

    followFormingCandle('bot-1', on);
    await settle();

    expect(builder.withUrl).toHaveBeenCalledWith('/api/hubs/bot-chart');
    expect(builder.withAutomaticReconnect).toHaveBeenCalled();
    expect(connection.invoke).toHaveBeenCalledWith('Subscribe', 'bot-1');
    expect(on.onSnapshot).toHaveBeenCalledWith(snapshot, false);
  });

  it('passes on the candles and the states the hub sends', async () => {
    const on = handlers();
    followFormingCandle('bot-1', on);
    await settle();

    connection.listeners.get('FormingCandle')!(candle);
    connection.listeners.get('FormingStatus')!('noData');

    expect(on.onCandle).toHaveBeenCalledWith(candle);
    expect(on.onStatus).toHaveBeenCalledWith('noData');
  });

  it('marks a snapshot that was answered after newer messages as superseded', async () => {
    const on = handlers();
    let answer: (value: FormingSnapshot) => void = () => undefined;
    connection.invoke.mockReturnValueOnce(new Promise((resolve) => (answer = resolve)));
    followFormingCandle('bot-1', on);
    await settle();

    connection.listeners.get('FormingCandle')!(candle);
    answer(snapshot);
    await settle();

    expect(on.onSnapshot).toHaveBeenCalledWith(snapshot, true);
  });

  it('reports a lost link and subscribes again once it is restored', async () => {
    const on = handlers();
    followFormingCandle('bot-1', on);
    await settle();

    connection.reconnecting();
    expect(on.onLost).toHaveBeenCalledTimes(1);

    connection.reconnected();
    await settle();
    expect(connection.invoke).toHaveBeenCalledTimes(2);
    expect(on.onSnapshot).toHaveBeenCalledTimes(2);

    connection.closed();
    expect(on.onLost).toHaveBeenCalledTimes(2);
  });

  it('reports no data when the connection or the subscription fails', async () => {
    const failed = handlers();
    connection.start.mockRejectedValueOnce(new Error('down'));
    followFormingCandle('bot-1', failed);
    await settle();
    expect(failed.onLost).toHaveBeenCalledTimes(1);

    connection = new FakeConnection();
    connection.invoke.mockRejectedValueOnce(new Error('bot_not_found'));
    const refused = handlers();
    followFormingCandle('bot-1', refused);
    await settle();
    expect(refused.onLost).toHaveBeenCalledTimes(1);
  });

  it('stops the connection and goes quiet when the page leaves', async () => {
    const on = handlers();
    const stop = followFormingCandle('bot-1', on);
    await settle();

    stop();
    connection.listeners.get('FormingCandle')!(candle);
    connection.closed();

    expect(connection.stop).toHaveBeenCalledTimes(1);
    expect(on.onCandle).not.toHaveBeenCalled();
    expect(on.onLost).not.toHaveBeenCalled();
  });

  it('never gives up reconnecting: 0, 2, 10, 30 seconds, then every 30 seconds', () => {
    followFormingCandle('bot-1', handlers());
    const policy = builder.withAutomaticReconnect.mock.calls[0]![0] as {
      nextRetryDelayInMilliseconds: (context: { previousRetryCount: number }) => number | null;
    };

    expect(
      [0, 1, 2, 3, 4, 50].map((count) =>
        policy.nextRetryDelayInMilliseconds({ previousRetryCount: count }),
      ),
    ).toEqual([0, 2000, 10_000, 30_000, 30_000, 30_000]);
  });

  describe('when the first answer fails', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('starts again after a pause until the connection is made, then subscribes', async () => {
      const on = handlers();
      connection.start
        .mockRejectedValueOnce(new Error('down'))
        .mockRejectedValueOnce(new Error('down'));

      followFormingCandle('bot-1', on);
      await vi.advanceTimersByTimeAsync(0);
      expect(on.onLost).toHaveBeenCalledTimes(1);
      expect(connection.start).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(2000);
      expect(connection.start).toHaveBeenCalledTimes(2);
      expect(connection.invoke).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(10_000);
      expect(connection.start).toHaveBeenCalledTimes(3);
      expect(connection.invoke).toHaveBeenCalledWith('Subscribe', 'bot-1');
      expect(on.onSnapshot).toHaveBeenCalledWith(snapshot, false);
    });

    it('subscribes again after a pause when the subscription is rejected', async () => {
      const on = handlers();
      connection.invoke.mockRejectedValueOnce(new Error('unavailable'));

      followFormingCandle('bot-1', on);
      await vi.advanceTimersByTimeAsync(0);
      expect(on.onLost).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(2000);
      expect(connection.start).toHaveBeenCalledTimes(1);
      expect(connection.invoke).toHaveBeenCalledTimes(2);
      expect(on.onSnapshot).toHaveBeenCalledWith(snapshot, false);
    });

    it('does not retry a bot the hub does not know', async () => {
      const on = handlers();
      connection.invoke.mockRejectedValue(
        new Error(
          "An unexpected error occurred invoking 'Subscribe' on the server. HubException: bot_not_found",
        ),
      );

      followFormingCandle('bot-1', on);
      await vi.advanceTimersByTimeAsync(120_000);

      expect(connection.invoke).toHaveBeenCalledTimes(1);
      expect(on.onLost).toHaveBeenCalledTimes(1);
    });

    it('starts again after the server closes the connection for good', async () => {
      const on = handlers();
      followFormingCandle('bot-1', on);
      await vi.advanceTimersByTimeAsync(0);
      expect(connection.start).toHaveBeenCalledTimes(1);

      connection.closed();
      expect(on.onLost).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(2000);

      expect(connection.start).toHaveBeenCalledTimes(2);
      expect(connection.invoke).toHaveBeenCalledTimes(2);
    });

    it('does not retry once the page has left', async () => {
      connection.start.mockRejectedValue(new Error('down'));

      const stop = followFormingCandle('bot-1', handlers());
      await vi.advanceTimersByTimeAsync(0);
      stop();
      await vi.advanceTimersByTimeAsync(120_000);

      expect(connection.start).toHaveBeenCalledTimes(1);
    });
  });
});
