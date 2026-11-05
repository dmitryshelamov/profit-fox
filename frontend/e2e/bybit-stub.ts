// A local stand-in for Bybit in end-to-end tests: deterministic instruments and candles over REST, the private
// calls about an account key, orders and their fills, and the v5 streams over WebSocket, so no test ever reaches the
// exchange. A test closes candles (`/control/candle`), sends a forming one (`/control/forming`), fills orders and drops the streams through the `/control/*` routes. Signatures are not checked: the key alone picks the
// answer (`e2e-valid-<uid>`, `e2e-withdraw`, anything else is rejected). The scanner also reads tickers, open interest and
// funding history here, and subscribes minute candles in batches. `/control/pump` starts a steep, loud rise of one symbol from the next minute, which the scanner's setups read as an impulse. Response shapes follow the integration tests' FakeBybitHandler.
// Usage: node e2e/bybit-stub.ts <port>
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';

import { type WebSocket, WebSocketServer } from 'ws';

const port = Number(process.argv[2] ?? '5296');
/** The date every instrument is listed at; candles exist from here on. */
const listedAt = Date.UTC(2025, 0, 1);
const minutes: Record<string, number> = {
  '1': 1,
  '3': 3,
  '5': 5,
  '15': 15,
  '30': 30,
  '60': 60,
  '120': 120,
  '240': 240,
  '360': 360,
  '720': 720,
  D: 1440,
  W: 10080,
};
const instruments = [
  { symbol: 'BTCUSDT', baseCoin: 'BTC', quoteCoin: 'USDT', basePrice: 100_000 },
  { symbol: 'ETHUSDT', baseCoin: 'ETH', quoteCoin: 'USDT', basePrice: 3_000 },
  { symbol: 'ETHBTC', baseCoin: 'ETH', quoteCoin: 'BTC', basePrice: 0.03 },
  // A whole number of saw-tooth periods fits an hour, so the others change by nothing over an hour; this one also creeps
  // up, so the market table has a pair that grows.
  { symbol: 'SOLUSDT', baseCoin: 'SOL', quoteCoin: 'USDT', basePrice: 150, trend: 1e-8 },
];
const categories = ['spot', 'linear'];
const linearPageSize = 2;
const fiveMinutes = 5 * 60_000;
const eightHours = 8 * 60 * 60_000;

/**
 * A saw-tooth around the base price with a 12-candle period, which an instrument with a trend tilts up by that share a
 * candle: no randomness, so every run sees the same candles.
 */
function price(instrument: { basePrice: number; trend?: number }, index: number): number {
  const phase = ((index % 12) + 12) % 12;
  const factor = 1 + ((phase < 6 ? phase : 12 - phase) - 3) / 100;

  return Number(
    (instrument.basePrice * factor * (1 + (instrument.trend ?? 0) * index)).toPrecision(8),
  );
}

/** A rise of one symbol that the market scanner reads as an impulse; it is started through `/control/pump`. */
let pump: { symbol: string; startIndex: number; minutes: number } | null = null;
const pumpStep = 1.03;
/** The volume of a pumped minute: 20 times a quiet one, so the volume of the window is far above the median. */
const pumpedVolume = 200;

/** How many minutes of the pump have passed by the minute with this index, the first pumped minute counting as one. */
function pumped(instrument: { symbol: string }, index: number): number {
  return pump?.symbol === instrument.symbol
    ? Math.min(Math.max(index - pump.startIndex + 1, 0), pump.minutes)
    : 0;
}

/** The price at the close of a minute: the saw-tooth, raised by a pump by 3 % a minute for its length and left there after. */
function minutePrice(
  instrument: { symbol: string; basePrice: number; trend?: number },
  index: number,
): number {
  return Number((price(instrument, index) * pumpStep ** pumped(instrument, index)).toPrecision(8));
}

/** The volume of a minute: 10, and loud while a pump runs. */
function minuteVolume(instrument: { symbol: string }, index: number): number {
  return pump?.symbol === instrument.symbol &&
    index >= pump.startIndex &&
    index < pump.startIndex + pump.minutes
    ? pumpedVolume
    : 10;
}

/** Starts a pump of `symbol` for `minutes` minutes from the next minute on. */
function startPump(url: URL) {
  const symbol = url.searchParams.get('symbol') ?? 'SOLUSDT';
  const startIndex = Math.floor((Date.now() - listedAt) / 60_000) + 1;
  pump = { symbol, startIndex, minutes: Number(url.searchParams.get('minutes') ?? '12') };

  return ok({ symbol, startIndex });
}

function send(response: ServerResponse, body: unknown, status = 200) {
  response.writeHead(status, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify(body));
}

function ok(result: unknown) {
  return { retCode: 0, retMsg: 'OK', result, retExtInfo: {}, time: Date.now() };
}

function klines(url: URL) {
  const category = url.searchParams.get('category') ?? '';
  const interval = minutes[url.searchParams.get('interval') ?? ''];
  const symbol = url.searchParams.get('symbol') ?? '';
  const instrument = instruments.find((candidate) => candidate.symbol === symbol);
  const start = Number(url.searchParams.get('start') ?? '0');
  const end = Number(url.searchParams.get('end') ?? String(Date.now()));
  const limit = Number(url.searchParams.get('limit') ?? '200');

  if (!interval || !instrument || !categories.includes(category)) {
    return { retCode: 10001, retMsg: 'params error: symbol invalid', result: {}, time: Date.now() };
  }

  const step = interval * 60_000;
  const closedBefore = Math.floor(Date.now() / step) * step;
  const rows: string[][] = [];

  // Bybit pages backwards: the newest candles inside [start, end] come first.
  for (
    let time = Math.floor(end / step) * step;
    time >= Math.max(start, listedAt) && rows.length < limit;
    time -= step
  ) {
    if (time + step > closedBefore) {
      continue;
    }

    const index = (time - listedAt) / step;
    // Only the minute candles the scanner reads carry a pump.
    const level = interval === 1 ? minutePrice : price;
    const volume = interval === 1 ? minuteVolume(instrument, index) : 10;
    const open = level(instrument, index - 1);
    const close = level(instrument, index);
    rows.push([
      String(time),
      String(open),
      String(Number((Math.max(open, close) * 1.001).toPrecision(8))),
      String(Number((Math.min(open, close) * 0.999).toPrecision(8))),
      String(close),
      String(volume),
      String(Number((volume * close).toPrecision(8))),
    ]);
  }

  return ok({ category, symbol, list: rows });
}

function instrumentsInfo(url: URL) {
  const category = url.searchParams.get('category') ?? '';
  const symbol = url.searchParams.get('symbol');

  if (!categories.includes(category)) {
    return {
      retCode: 10001,
      retMsg: 'params error: category invalid',
      result: {},
      time: Date.now(),
    };
  }

  const rules = (instrument: { basePrice: number }) =>
    category === 'spot'
      ? {
          priceFilter: { tickSize: instrument.basePrice < 1 ? '0.00001' : '0.01' },
          lotSizeFilter: {
            basePrecision: '0.000001',
            quotePrecision: '0.00000001',
            minOrderQty: '0.000048',
            maxOrderQty: '1000',
            minOrderAmt: '5',
            maxOrderAmt: '2000000',
          },
        }
      : {
          priceFilter: {
            minPrice: '0.00001',
            maxPrice: '1999999',
            tickSize: instrument.basePrice < 1 ? '0.00001' : '0.1',
          },
          lotSizeFilter: {
            maxOrderQty: '100',
            minOrderQty: '0.001',
            qtyStep: '0.001',
            minNotionalValue: '5',
          },
        };
  const describe = (instrument: (typeof instruments)[number]) => ({
    symbol: instrument.symbol,
    baseCoin: instrument.baseCoin,
    quoteCoin: instrument.quoteCoin,
    status: 'Trading',
    ...(category === 'linear'
      ? { contractType: 'LinearPerpetual', launchTime: String(listedAt) }
      : {}),
    ...rules(instrument),
  });

  if (symbol) {
    return ok({
      category,
      list: instruments.filter((instrument) => instrument.symbol === symbol).map(describe),
    });
  }

  // Spot answers in one page; linear pages by cursor, like the real list.
  const all = [...instruments].sort((a, b) => (a.symbol < b.symbol ? -1 : 1));
  const pageSize = category === 'linear' ? linearPageSize : all.length;
  const offset = Number(url.searchParams.get('cursor') || '0');
  const cursor =
    category === 'linear' && offset + pageSize < all.length ? String(offset + pageSize) : '';

  return ok({
    category,
    list: all.slice(offset, offset + pageSize).map(describe),
    nextPageCursor: cursor,
  });
}

/** The tickers of every instrument of the category at the close of the last minute; linear ones with open interest and funding. */
function tickers(url: URL) {
  const category = url.searchParams.get('category') ?? '';
  const index = Math.floor((Date.now() - listedAt) / 60_000);

  return ok({
    category,
    list: instruments.map((instrument) => {
      const last = minutePrice(instrument, index);
      const linear =
        category === 'linear'
          ? {
              openInterest: String(1000 + (index % 12)),
              openInterestValue: '0',
              fundingRate: '0.0001',
              markPrice: String(last),
              indexPrice: String(last),
              nextFundingTime: '0',
            }
          : {};

      return {
        symbol: instrument.symbol,
        lastPrice: String(last),
        bid1Price: String(Number((last * 0.9999).toPrecision(8))),
        bid1Size: '1',
        ask1Price: String(Number((last * 1.0001).toPrecision(8))),
        ask1Size: '1',
        prevPrice24h: String(last),
        price24hPcnt: '0',
        highPrice24h: String(last),
        lowPrice24h: String(last),
        turnover24h: '0',
        volume24h: '0',
        ...linear,
      };
    }),
  });
}

/** Times every `step` from the listing inside the inclusive [startTime, endTime], newest first. */
function times(url: URL, step: number): number[] {
  const start = Math.max(Number(url.searchParams.get('startTime') ?? '0'), listedAt);
  const end = Math.min(Number(url.searchParams.get('endTime') ?? String(Date.now())), Date.now());
  const all: number[] = [];

  for (let time = Math.floor(end / step) * step; time >= start; time -= step) {
    all.push(time);
  }

  return all;
}

/** Open interest every 5 minutes in coins, a page at a time by cursor, like Bybit. */
function openInterest(url: URL) {
  const limit = Number(url.searchParams.get('limit') ?? '50');
  const offset = Number(url.searchParams.get('cursor') || '0');
  const all = times(url, fiveMinutes);

  return ok({
    category: 'linear',
    symbol: url.searchParams.get('symbol'),
    list: all.slice(offset, offset + limit).map((time) => ({
      openInterest: String(1000 + (((time - listedAt) / fiveMinutes) % 12)),
      timestamp: String(time),
    })),
    nextPageCursor: offset + limit < all.length ? String(offset + limit) : '',
  });
}

/** A settlement every 8 hours, newest first and at most `limit`: Bybit pages funding history by time only. */
function funding(url: URL) {
  const limit = Number(url.searchParams.get('limit') ?? '200');

  return ok({
    category: 'linear',
    list: times(url, eightHours)
      .slice(0, limit)
      .map((time) => ({
        symbol: url.searchParams.get('symbol'),
        fundingRate: (time / eightHours) % 2 === 0 ? '0.0001' : '-0.00005',
        fundingRateTimestamp: String(time),
      })),
  });
}

/** What Bybit tells about a key: a unified account that trades spot and linear, without withdrawals unless asked. */
function apiKeyInfo(key: string) {
  const valid = /^e2e-valid-(\d+)$/.exec(key);

  if (!valid && key !== 'e2e-withdraw') {
    return {
      retCode: 10003,
      retMsg: 'API key is invalid.',
      result: {},
      retExtInfo: {},
      time: Date.now(),
    };
  }

  return ok({
    id: '1',
    note: 'e2e',
    apiKey: key,
    readOnly: 0,
    secret: '',
    permissions: {
      ContractTrade: ['Order', 'Position'],
      Spot: ['SpotTrade'],
      Wallet: valid ? ['AccountTransfer'] : ['AccountTransfer', 'Withdraw'],
    },
    ips: ['*'],
    type: 1,
    unified: 0,
    uta: 1,
    userID: valid ? Number(valid[1]) : 1,
    isMaster: true,
  });
}

/** The same balance for every account: 1000 USDT and 0.01 BTC, with what the fills of orders bought and sold. */
function walletBalance(key: string) {
  if (!key.startsWith('e2e-valid-')) {
    return {
      retCode: 10003,
      retMsg: 'API key is invalid.',
      result: {},
      retExtInfo: {},
      time: Date.now(),
    };
  }

  return ok({
    list: [
      {
        accountType: 'UNIFIED',
        totalEquity: '1675.31',
        coin: Object.entries(coins).map(([coin, balance]) => ({
          coin,
          walletBalance: String(balance),
          locked: '0',
          usdValue: coin === 'USDT' ? '1000.25' : '675.06',
        })),
      },
    ],
  });
}

/** What every account holds; the fills of orders change it. */
const coins: Record<string, number> = { USDT: 1000, BTC: 0.01 };

interface StubOrder {
  orderId: string;
  orderLinkId: string;
  category: string;
  symbol: string;
  side: string;
  orderType: string;
  price: string;
  qty: string;
  orderStatus: string;
  cumExecQty: string;
  cumExecValue: string;
  /** The unit of `qty` of a spot market order, as Bybit reads it: a buy without one is sized in the quote coin. */
  marketUnit: string;
}

const orders = new Map<string, StubOrder>();
const executions = new Map<string, object[]>();
let execs = 0;
const sockets = new Map<WebSocket, { path: string; topics: Set<string> }>();

function push(topic: string, data: object[]) {
  const message = JSON.stringify({
    topic,
    id: String(Date.now()),
    type: 'snapshot',
    ts: Date.now(),
    creationTime: Date.now(),
    data,
  });

  for (const [socket, connection] of sockets) {
    if (connection.topics.has(topic)) {
      socket.send(message);
    }
  }
}

/** The unfilled quantity of an order in its own unit; nothing for a finished one. */
function leaves(order: StubOrder): string {
  if (order.orderStatus !== 'New' && order.orderStatus !== 'PartiallyFilled') {
    return '0';
  }

  return String(
    Number(order.qty) -
      Number(order.marketUnit === 'quoteCoin' ? order.cumExecValue : order.cumExecQty),
  );
}

function orderView(order: StubOrder) {
  return {
    ...order,
    timeInForce: 'GTC',
    leavesQty: leaves(order),
    // The stub takes no fee; the totals of the order are what a bot books its fills by.
    cumExecFee: '0',
    feeCurrency: 'USDT',
    rejectReason: 'EC_NoError',
    createdTime: String(Date.now()),
    updatedTime: String(Date.now()),
  };
}

async function body(request: IncomingMessage): Promise<Record<string, unknown>> {
  let text = '';

  for await (const chunk of request) {
    text += String(chunk);
  }

  return text ? (JSON.parse(text) as Record<string, unknown>) : {};
}

function createOrder(request: Record<string, unknown>) {
  const orderLinkId = String(request.orderLinkId);
  const known = orders.get(orderLinkId);

  if (known) {
    return {
      retCode: 110072,
      retMsg: 'OrderLinkedID is duplicate',
      result: {},
      retExtInfo: {},
      time: Date.now(),
    };
  }

  const order: StubOrder = {
    orderId: `x-${orderLinkId}`,
    orderLinkId,
    category: String(request.category),
    symbol: String(request.symbol),
    side: String(request.side),
    orderType: String(request.orderType),
    price: String(request.price ?? '0'),
    qty: String(request.qty),
    orderStatus: 'New',
    cumExecQty: '0',
    cumExecValue: '0',
    marketUnit:
      request.category === 'spot' && request.orderType === 'Market'
        ? String(request.marketUnit ?? (request.side === 'Buy' ? 'quoteCoin' : 'baseCoin'))
        : '',
  };
  orders.set(orderLinkId, order);
  push('order', [orderView(order)]);

  return ok({ orderId: order.orderId, orderLinkId });
}

function cancelOrder(request: Record<string, unknown>) {
  const order = orders.get(String(request.orderLinkId));

  if (!order || order.orderStatus !== 'New') {
    return {
      retCode: 110001,
      retMsg: 'order not exists or too late to cancel',
      result: {},
      retExtInfo: {},
      time: Date.now(),
    };
  }

  order.orderStatus = 'Cancelled';
  push('order', [orderView(order)]);

  return ok({ orderId: order.orderId, orderLinkId: order.orderLinkId });
}

function listOrders(url: URL, history: boolean) {
  const link = url.searchParams.get('orderLinkId');
  const all = [...orders.values()].filter(
    (order) =>
      (link ? order.orderLinkId === link : true) &&
      (history ? order.orderStatus !== 'New' : link ? true : order.orderStatus === 'New'),
  );

  return ok({
    category: url.searchParams.get('category'),
    list: all.map(orderView),
    nextPageCursor: '',
  });
}

/**
 * Fills an order as the exchange would and tells the private streams. A spot market order sized in the quote coin buys
 * what its amount pays for at the price; a finished order or one filled in full takes no more fills.
 */
function fill(url: URL) {
  const order = orders.get(url.searchParams.get('orderLinkId') ?? '');

  if (!order || (order.orderStatus !== 'New' && order.orderStatus !== 'PartiallyFilled')) {
    return { retCode: 10001, retMsg: 'No such open order', result: {} };
  }

  const price = Number(url.searchParams.get('price') ?? '100000');
  const byValue = order.marketUnit === 'quoteCoin';
  const left = byValue
    ? (Number(order.qty) - Number(order.cumExecValue)) / price
    : Number(order.qty) - Number(order.cumExecQty);
  const qty = Number(url.searchParams.get('qty') ?? left);

  if (qty > left + 1e-12) {
    return { retCode: 10001, retMsg: 'Fill exceeds the order', result: {} };
  }

  order.cumExecQty = String(Number(order.cumExecQty) + qty);
  order.cumExecValue = String(Number(order.cumExecValue) + qty * price);
  order.orderStatus = qty >= left - 1e-12 ? 'Filled' : 'PartiallyFilled';
  const base = order.symbol.replace(/USDT$/, '');
  coins[base] = (coins[base] ?? 0) + (order.side === 'Buy' ? qty : -qty);
  coins.USDT = (coins.USDT ?? 0) + (order.side === 'Buy' ? -qty * price : qty * price);
  const execution = {
    category: order.category,
    symbol: order.symbol,
    orderId: order.orderId,
    orderLinkId: order.orderLinkId,
    side: order.side,
    orderType: order.orderType,
    orderPrice: order.price,
    orderQty: order.qty,
    leavesQty: leaves(order),
    execId: `e-${++execs}`,
    execQty: String(qty),
    execPrice: String(price),
    execFee: '0',
    feeCurrency: 'USDT',
    execType: 'Trade',
    execValue: String(qty * price),
    execTime: String(Date.now()),
    isMaker: false,
    seq: execs,
  };
  executions.set(order.orderLinkId, [...(executions.get(order.orderLinkId) ?? []), execution]);

  // A silent fill is missed by the streams, as during a drop: only the history has it.
  if (url.searchParams.get('silent') !== '1') {
    push('execution', [execution]);
    push('order', [orderView(order)]);
  }

  return ok(execution);
}

/** Sends a kline update of one candle to the subscribed sockets: the closing when `confirm` is set, otherwise a forming one. */
function candle(url: URL, confirm = true) {
  const interval = url.searchParams.get('interval') ?? '60';
  const start = Number(url.searchParams.get('start'));
  const close = url.searchParams.get('close') ?? '100000';
  const volume = url.searchParams.get('volume') ?? '1';
  push(`kline.${interval}.${url.searchParams.get('symbol') ?? 'BTCUSDT'}`, [
    {
      start,
      end: start + (minutes[interval] ?? 60) * 60_000 - 1,
      interval,
      open: close,
      close,
      high: close,
      low: close,
      volume,
      turnover: close,
      confirm,
      timestamp: Date.now(),
    },
  ]);

  return ok({});
}

function answer(socket: WebSocket, connection: { topics: Set<string> }, raw: string) {
  const request = JSON.parse(raw) as { op?: string; req_id?: string; args?: string[] };
  const reply = (op: string, extra: object = {}) =>
    socket.send(
      JSON.stringify({
        success: true,
        ret_msg: '',
        op,
        conn_id: 'e2e',
        req_id: request.req_id,
        ...extra,
      }),
    );

  if (request.op === 'auth') {
    reply('auth');
  } else if (request.op === 'subscribe') {
    for (const topic of request.args ?? []) {
      connection.topics.add(topic);
    }

    reply('subscribe');
  } else if (request.op === 'unsubscribe') {
    for (const topic of request.args ?? []) {
      connection.topics.delete(topic);
    }

    reply('unsubscribe');
  } else if (request.op === 'ping') {
    reply('ping', { ret_msg: 'pong' });
  }
}

const streams = new WebSocketServer({ noServer: true });

const server = createServer((request, response) => {
  const url = new URL(request.url ?? '/', `http://127.0.0.1:${port}`);

  if (url.pathname === '/health') {
    send(response, { ok: true });
  } else if (url.pathname === '/v5/market/kline') {
    send(response, klines(url));
  } else if (url.pathname === '/v5/market/tickers') {
    send(response, tickers(url));
  } else if (url.pathname === '/v5/market/open-interest') {
    send(response, openInterest(url));
  } else if (url.pathname === '/v5/market/funding/history') {
    send(response, funding(url));
  } else if (url.pathname === '/v5/market/instruments-info') {
    send(response, instrumentsInfo(url));
  } else if (url.pathname === '/v5/user/query-api') {
    send(response, apiKeyInfo(String(request.headers['x-bapi-api-key'] ?? '')));
  } else if (url.pathname === '/v5/account/wallet-balance') {
    send(response, walletBalance(String(request.headers['x-bapi-api-key'] ?? '')));
  } else if (url.pathname === '/v5/account/fee-rate') {
    send(
      response,
      ok({
        list: [{ symbol: url.searchParams.get('symbol'), takerFeeRate: '0', makerFeeRate: '0' }],
      }),
    );
  } else if (url.pathname === '/v5/position/list') {
    send(response, ok({ category: 'linear', list: [], nextPageCursor: '' }));
  } else if (url.pathname === '/v5/order/realtime') {
    send(response, listOrders(url, false));
  } else if (url.pathname === '/v5/order/history') {
    send(response, listOrders(url, true));
  } else if (url.pathname === '/v5/execution/list') {
    const link = url.searchParams.get('orderLinkId') ?? '';
    send(
      response,
      ok({
        category: url.searchParams.get('category'),
        list: executions.get(link) ?? [],
        nextPageCursor: '',
      }),
    );
  } else if (url.pathname === '/v5/order/create' || url.pathname === '/v5/order/cancel') {
    void body(request).then((parsed) =>
      send(response, url.pathname.endsWith('create') ? createOrder(parsed) : cancelOrder(parsed)),
    );
  } else if (url.pathname === '/control/fill') {
    send(response, fill(url));
  } else if (url.pathname === '/control/candle') {
    send(response, candle(url));
  } else if (url.pathname === '/control/forming') {
    send(response, candle(url, false));
  } else if (url.pathname === '/control/pump') {
    send(response, startPump(url));
  } else if (url.pathname === '/control/drop') {
    for (const socket of sockets.keys()) {
      socket.terminate();
    }

    send(response, ok({}));
  } else if (url.pathname === '/control/orders') {
    send(response, [...orders.values()]);
  } else if (url.pathname === '/v5/market/time') {
    const now = Date.now();
    send(response, {
      retCode: 0,
      retMsg: 'OK',
      result: { timeSecond: String(Math.floor(now / 1000)), timeNano: String(now * 1_000_000) },
      retExtInfo: {},
      time: now,
    });
  } else {
    send(response, { retCode: 10001, retMsg: `Not stubbed: ${url.pathname}`, result: {} }, 404);
  }
});

server.on('upgrade', (request, socket, head) => {
  const path = new URL(request.url ?? '/', `http://127.0.0.1:${port}`).pathname;
  streams.handleUpgrade(request, socket, head, (client) => {
    const connection = { path, topics: new Set<string>() };
    sockets.set(client, connection);
    client.on('message', (raw) => answer(client, connection, String(raw)));
    client.on('close', () => sockets.delete(client));
  });
});

/** Closes the minute that just ended on every subscribed `kline.1` topic, as Bybit does, so the scanner's minutes keep up. */
let closedMinute = Math.floor(Date.now() / 60_000) * 60_000;

setInterval(() => {
  const minute = Math.floor(Date.now() / 60_000) * 60_000;

  if (minute === closedMinute) {
    return;
  }

  closedMinute = minute;
  const start = minute - 60_000;
  const index = (start - listedAt) / 60_000;

  for (const [socket, connection] of sockets) {
    for (const topic of connection.topics) {
      const instrument = instruments.find((candidate) => `kline.1.${candidate.symbol}` === topic);

      if (!instrument) {
        continue;
      }

      const open = minutePrice(instrument, index - 1);
      const close = minutePrice(instrument, index);
      const volume = minuteVolume(instrument, index);
      socket.send(
        JSON.stringify({
          topic,
          id: String(Date.now()),
          type: 'snapshot',
          ts: Date.now(),
          creationTime: Date.now(),
          data: [
            {
              start,
              end: minute - 1,
              interval: '1',
              open: String(open),
              close: String(close),
              high: String(Math.max(open, close)),
              low: String(Math.min(open, close)),
              volume: String(volume),
              turnover: String(Number((volume * close).toPrecision(8))),
              confirm: true,
              timestamp: Date.now(),
            },
          ],
        }),
      );
    }
  }
}, 500);

server.listen(port, '127.0.0.1');
