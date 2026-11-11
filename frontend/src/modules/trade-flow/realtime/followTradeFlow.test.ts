import { HubConnectionBuilder } from '@microsoft/signalr';

import type { LiveMinute, LiveSnapshot } from '../model/tradeFlow';
import { followTradeFlow } from './followTradeFlow';

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(),
  LogLevel: { None: 6 },
}));

const minute: LiveMinute = {
  openTime: '2026-11-11T12:04:00Z',
  buyVolume: '2',
  sellVolume: '1',
  buyTurnover: '300',
  sellTurnover: '149',
  buyTrades: 1,
  sellTrades: 1,
  since: '2026-11-11T12:04:30Z',
};
const snapshot: LiveSnapshot = { state: 'live', flow: minute };

let listeners: Map<string, (message: unknown) => void>;
let invoke: ReturnType<typeof vi.fn>;
let withUrl: ReturnType<typeof vi.fn>;

const settle = () => new Promise((resolve) => setTimeout(resolve));

beforeEach(() => {
  listeners = new Map();
  invoke = vi.fn(() => Promise.resolve(snapshot));
  withUrl = vi.fn();
  const connection = {
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
    invoke,
    on: (name: string, listener: (message: unknown) => void) => void listeners.set(name, listener),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
  };
  const builder = {
    withUrl: withUrl.mockImplementation(() => builder),
    withAutomaticReconnect: () => builder,
    configureLogging: () => builder,
    build: () => connection,
  };
  vi.mocked(HubConnectionBuilder).mockImplementation(function () {
    return builder as never;
  });
});

describe('followTradeFlow', () => {
  it('joins the pair on the hub of the trade flow and hands on its messages', async () => {
    const on = { onSnapshot: vi.fn(), onMinute: vi.fn(), onStatus: vi.fn(), onLost: vi.fn() };

    const stop = followTradeFlow('linear', 'SOLUSDT', on);
    await settle();
    listeners.get('TradeFlowMinute')!(minute);
    listeners.get('TradeFlowStatus')!('noData');
    stop();

    expect(withUrl).toHaveBeenCalledWith('/api/hubs/trade-flow');
    expect(invoke).toHaveBeenCalledWith('Join', 'linear', 'SOLUSDT');
    expect(on.onSnapshot).toHaveBeenCalledWith(snapshot, false);
    expect(on.onMinute).toHaveBeenCalledWith(minute);
    expect(on.onStatus).toHaveBeenCalledWith('noData');
  });

  it('does not try again a pair the hub has no trade flow for', async () => {
    vi.useFakeTimers();
    invoke.mockImplementation(() =>
      Promise.reject(new Error('HubException: trade_flow_not_found')),
    );
    const on = { onSnapshot: vi.fn(), onMinute: vi.fn(), onStatus: vi.fn(), onLost: vi.fn() };

    followTradeFlow('moex', 'SBER', on);
    await vi.advanceTimersByTimeAsync(60_000);
    vi.useRealTimers();

    expect(invoke).toHaveBeenCalledTimes(1);
    expect(on.onLost).toHaveBeenCalled();
  });
});
