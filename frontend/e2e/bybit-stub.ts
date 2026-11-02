// A local stand-in for Bybit in end-to-end tests: deterministic instruments and candles over REST, and the private
// calls about an account key, so no test ever reaches the exchange. Signatures are not checked: the key alone picks the
// answer (`e2e-valid-<uid>`, `e2e-withdraw`, anything else is rejected). Response shapes follow the integration tests' FakeBybitHandler.
// Usage: node e2e/bybit-stub.ts <port>
import { createServer, type ServerResponse } from 'node:http';

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
  { symbol: 'SOLUSDT', baseCoin: 'SOL', quoteCoin: 'USDT', basePrice: 150 },
];
const categories = ['spot', 'linear'];
const linearPageSize = 2;

/** A saw-tooth around the base price with a 12-candle period: no randomness, so every run sees the same candles. */
function price(basePrice: number, index: number): number {
  const phase = ((index % 12) + 12) % 12;
  const factor = 1 + ((phase < 6 ? phase : 12 - phase) - 3) / 100;

  return Number((basePrice * factor).toPrecision(8));
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
    const open = price(instrument.basePrice, index - 1);
    const close = price(instrument.basePrice, index);
    rows.push([
      String(time),
      String(open),
      String(Number((Math.max(open, close) * 1.001).toPrecision(8))),
      String(Number((Math.min(open, close) * 0.999).toPrecision(8))),
      String(close),
      '10',
      String(Number((10 * close).toPrecision(8))),
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

/** The same balance for every account: 1000 USDT and 0.01 BTC. */
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
        coin: [
          { coin: 'USDT', walletBalance: '1000', usdValue: '1000.25' },
          { coin: 'BTC', walletBalance: '0.01', usdValue: '675.06' },
        ],
      },
    ],
  });
}

const server = createServer((request, response) => {
  const url = new URL(request.url ?? '/', `http://127.0.0.1:${port}`);

  if (url.pathname === '/health') {
    send(response, { ok: true });
  } else if (url.pathname === '/v5/market/kline') {
    send(response, klines(url));
  } else if (url.pathname === '/v5/market/instruments-info') {
    send(response, instrumentsInfo(url));
  } else if (url.pathname === '/v5/user/query-api') {
    send(response, apiKeyInfo(String(request.headers['x-bapi-api-key'] ?? '')));
  } else if (url.pathname === '/v5/account/wallet-balance') {
    send(response, walletBalance(String(request.headers['x-bapi-api-key'] ?? '')));
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

server.listen(port, '127.0.0.1');
