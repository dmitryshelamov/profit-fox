import { type APIRequestContext, expect, test } from '@playwright/test';

// The stub of Bybit itself: an entry of the linear market with a stop and a target in the Partial mode makes the two orders
// Bybit makes for it, a price from `/control/price` fires one of them, and the position of the symbol follows. No UI is driven.

const stub = 'http://127.0.0.1:5296';
const key = { 'X-BAPI-API-KEY': 'e2e-valid-1' };

interface Order {
  orderId: string;
  orderLinkId: string;
  parentOrderLinkId?: string;
  stopOrderType?: string;
  orderStatus: string;
  side: string;
  qty: string;
  cumExecQty: string;
  timeInForce?: string;
  reduceOnly?: boolean;
}

async function create(request: APIRequestContext, fields: Record<string, unknown>) {
  const response = await request.post(`${stub}/v5/order/create`, {
    headers: key,
    data: { category: 'linear', symbol: 'ETHUSDT', ...fields },
  });

  return (await response.json()) as { retCode: number; retMsg: string };
}

async function orders(request: APIRequestContext, query: string) {
  const response = await request.get(
    `${stub}/v5/order/realtime?category=linear&symbol=ETHUSDT&${query}`,
    {
      headers: key,
    },
  );

  return ((await response.json()) as { result: { list: Order[] } }).result.list;
}

async function position(request: APIRequestContext): Promise<{ side: string; size: string }[]> {
  const response = await request.get(`${stub}/v5/position/list?category=linear&symbol=ETHUSDT`, {
    headers: key,
  });

  return ((await response.json()) as { result: { list: { side: string; size: string }[] } }).result
    .list;
}

test.afterEach(async ({ request }) => {
  await request.get(`${stub}/control/price?symbol=ETHUSDT`);
});

test('an entry with a stop and a target makes two orders, the price fires one, and the position follows', async ({
  request,
}) => {
  const link = `i-stub-${Date.now()}`;
  await request.get(`${stub}/control/price?symbol=ETHUSDT&price=3000`);

  expect(
    (
      await create(request, {
        side: 'Buy',
        orderType: 'Market',
        qty: '0.5',
        orderLinkId: link,
        takeProfit: '3100',
        stopLoss: '2900',
        tpslMode: 'Partial',
        tpOrderType: 'Market',
        slOrderType: 'Market',
        tpTriggerBy: 'LastPrice',
        slTriggerBy: 'LastPrice',
        positionIdx: 0,
      })
    ).retCode,
  ).toBe(0);
  expect(await position(request)).toEqual([]);

  await request.get(`${stub}/control/fill?orderLinkId=${link}&price=3000`);

  const made = (await orders(request, '')).filter((order) => order.parentOrderLinkId === link);
  expect(made.map((order) => order.stopOrderType).sort()).toEqual([
    'PartialStopLoss',
    'PartialTakeProfit',
  ]);
  expect(made.every((order) => order.side === 'Sell' && order.qty === '0.5')).toBe(true);
  expect((await position(request))[0]).toMatchObject({ side: 'Buy', size: '0.5' });

  await request.get(`${stub}/control/price?symbol=ETHUSDT&price=3100`);

  const firstId = made.find((order) => order.stopOrderType === 'PartialTakeProfit')?.orderId ?? '';
  const target = (await orders(request, `orderId=${firstId}`))[0];
  const history = (await (
    await request.get(
      `${stub}/v5/order/history?category=linear&symbol=ETHUSDT&orderId=${firstId}`,
      {
        headers: key,
      },
    )
  ).json()) as { result: { list: Order[] } };
  const fired = [target, ...history.result.list].find((order) => order?.orderStatus === 'Filled');
  expect(fired?.stopOrderType).toBe('PartialTakeProfit');
  expect(await orders(request, '')).toEqual([]);
  expect(await position(request)).toEqual([]);
});

test('a limit order that is IOC and cannot fill at once is cancelled, and a reduce-only order needs a position', async ({
  request,
}) => {
  await request.get(`${stub}/control/price?symbol=ETHUSDT&price=3000`);
  const ioc = `i-ioc-${Date.now()}`;

  expect(
    (
      await create(request, {
        side: 'Buy',
        orderType: 'Limit',
        price: '2900',
        qty: '0.5',
        timeInForce: 'IOC',
        orderLinkId: ioc,
      })
    ).retCode,
  ).toBe(0);
  const history = (await (
    await request.get(
      `${stub}/v5/order/history?category=linear&symbol=ETHUSDT&orderLinkId=${ioc}`,
      {
        headers: key,
      },
    )
  ).json()) as { result: { list: Order[] } };
  expect(history.result.list[0]?.orderStatus).toBe('Cancelled');

  const refused = await create(request, {
    side: 'Sell',
    orderType: 'Market',
    qty: '0.5',
    reduceOnly: true,
    orderLinkId: `i-reduce-${Date.now()}`,
  });
  expect(refused.retCode).toBe(110017);
});
