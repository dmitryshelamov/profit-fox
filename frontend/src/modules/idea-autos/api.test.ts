import { QueryClient } from '@tanstack/react-query';

import { fakeApi, json } from '@/test/fakeApi';

import {
  autoQuery,
  autosQuery,
  deleteAuto,
  guardsQuery,
  refusalsQuery,
  saveAuto,
  setAutoMode,
  setAutosOrder,
} from './api';
import { formOfDefaults } from './model/autoForm';

const form = {
  ...formOfDefaults({ accountId: 'account-1', amount: '300', minRiskReward: '1.5' }),
  name: 'Импульс лонг',
  setups: ['impulse'],
  types: ['pullback'],
  intervals: ['15'],
};

const noContent = () => new Response(null, { status: 204 });

describe('requests of autos', () => {
  it('reads the list, one auto and the guards', async () => {
    const requests = fakeApi({
      'GET /api/idea-autos': () => json([]),
      'GET /api/idea-autos/auto-1': () => json({ id: 'auto-1' }),
      'GET /api/idea-trading/guards': () => json({ openTrades: 0 }),
    });
    const client = new QueryClient();

    expect(await client.fetchQuery(autosQuery())).toEqual([]);
    expect(await client.fetchQuery(autoQuery('auto-1'))).toEqual({ id: 'auto-1' });
    expect(await client.fetchQuery(guardsQuery())).toEqual({ openTrades: 0 });
    expect(requests.map((request) => new URL(request.url).pathname)).toEqual([
      '/api/idea-autos',
      '/api/idea-autos/auto-1',
      '/api/idea-trading/guards',
    ]);
  });

  it('asks for the refusals by the period, the page and the size, and for one auto only when it is named', async () => {
    const requests = fakeApi({ 'GET /api/idea-autos/refusals': () => json({ items: [] }) });
    const client = new QueryClient();

    await client.fetchQuery(refusalsQuery({ period: 'today', autoId: '', page: 1, pageSize: 50 }));
    await client.fetchQuery(
      refusalsQuery({ period: '7d', autoId: 'auto-1', page: 2, pageSize: 100 }),
    );

    expect(requests.map((request) => new URL(request.url).search)).toEqual([
      '?period=today&page=1&pageSize=50',
      '?period=7d&autoId=auto-1&page=2&pageSize=100',
    ]);
  });

  it('creates an auto with the body of the form, and switches it on when the form says so', async () => {
    const requests = fakeApi({
      'POST /api/idea-autos': () => json({ id: 'auto-1' }, 201),
      'PUT /api/idea-autos/auto-1/mode': noContent,
    });

    const id = await saveAuto({ id: null, form: { ...form, mode: 'live' }, savedMode: 'off' });

    expect(id).toBe('auto-1');
    expect(await requests[0]!.text()).toBe(
      '{"name":"Импульс лонг","accountId":"account-1","amount":300,"budget":null,"setups":["impulse"],"types":["pullback"],"intervals":["15"],"side":null,"category":null,"badge":null,"minRiskReward":1.5,"minCases":0,"dayDrop":"any","btcDrop":"any","highFunding":"any","burst":"any","maxPerMinutePerSide":null,"maxOpenPerSide":null}',
    );
    expect(await requests[1]!.text()).toBe('{"mode":"live"}');
  });

  it('creates an auto that stays off with one request', async () => {
    const requests = fakeApi({ 'POST /api/idea-autos': () => json({ id: 'auto-1' }, 201) });

    await saveAuto({ id: null, form, savedMode: 'off' });

    expect(requests).toHaveLength(1);
  });

  it('saves an auto and changes its mode only when the mode changed', async () => {
    const requests = fakeApi({
      'PUT /api/idea-autos/auto-1': noContent,
      'PUT /api/idea-autos/auto-1/mode': noContent,
    });

    await saveAuto({ id: 'auto-1', form: { ...form, mode: 'paper' }, savedMode: 'paper' });
    await saveAuto({ id: 'auto-1', form: { ...form, mode: 'live' }, savedMode: 'paper' });

    expect(requests.map((request) => new URL(request.url).pathname)).toEqual([
      '/api/idea-autos/auto-1',
      '/api/idea-autos/auto-1',
      '/api/idea-autos/auto-1/mode',
    ]);
  });

  it('changes the mode, saves the order and deletes an auto', async () => {
    const requests = fakeApi({
      'PUT /api/idea-autos/auto-1/mode': noContent,
      'PUT /api/idea-autos/order': noContent,
      'DELETE /api/idea-autos/auto-1': noContent,
    });

    await setAutoMode('auto-1', 'paper');
    await setAutosOrder(['auto-2', 'auto-1']);
    await deleteAuto('auto-1');

    expect(await requests[0]!.text()).toBe('{"mode":"paper"}');
    expect(await requests[1]!.text()).toBe('{"ids":["auto-2","auto-1"]}');
    expect(requests[2]!.method).toBe('DELETE');
  });
});
