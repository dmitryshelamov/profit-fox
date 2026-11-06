import { QueryClient } from '@tanstack/react-query';

import { fakeApi, json } from '@/test/fakeApi';

import {
  cancelTrade,
  closeTrade,
  enterIdea,
  ideaEntryQuery,
  ideaJournalQuery,
  noJournalFilters,
  saveSettings,
  tradingSettingsQuery,
} from './api';

const settings = {
  accountId: null,
  risk: '1',
  maxAmount: '200',
  maxOpenTrades: 5,
  maxTotalRisk: '5',
  minRiskReward: '1.5',
};

describe('requests of trades by ideas', () => {
  it('reads the journal and the settings', async () => {
    const journal = { open: [], closed: { items: [], page: 1, pageSize: 50, total: 0 } };
    const requests = fakeApi({
      'GET /api/idea-trades': () => json(journal),
      'GET /api/idea-trading/settings': () => json(settings),
    });
    const client = new QueryClient();

    expect(
      await client.fetchQuery(
        ideaJournalQuery({ filters: noJournalFilters, page: 1, pageSize: 50 }),
      ),
    ).toEqual(journal);
    expect(await client.fetchQuery(tradingSettingsQuery())).toEqual(settings);
    expect(requests.map((request) => request.method)).toEqual(['GET', 'GET']);
  });

  // Requirement "Фильтры и листание сделок": the API gets the filters chosen, the page and its size; a filter left empty is not sent.
  it('asks the journal for the filters, the page and the size, and leaves out a filter that is empty', async () => {
    const requests = fakeApi({
      'GET /api/idea-trades': () => json({}),
    });
    const client = new QueryClient();

    await client.fetchQuery(ideaJournalQuery({ filters: noJournalFilters, page: 1, pageSize: 50 }));
    await client.fetchQuery(
      ideaJournalQuery({
        filters: { setup: 'drop', symbol: 'eth', side: 'short', badge: 'none', days: '30' },
        page: 3,
        pageSize: 100,
      }),
    );

    expect(requests.map((request) => new URL(request.url).search)).toEqual([
      '?Page=1&PageSize=50',
      '?Setup=drop&Symbol=eth&Side=short&Badge=none&Days=30&Page=3&PageSize=100',
    ]);
  });

  it('asks for the entry of an idea with the account and the risk typed, and without them leaves them to the settings', async () => {
    const requests = fakeApi({
      'GET /api/ideas/idea-1/entry': () => json({ reasons: [] }),
    });
    const client = new QueryClient();

    await client.fetchQuery(ideaEntryQuery('idea-1', 'account-1', '1.5'));
    await client.fetchQuery(ideaEntryQuery('idea-1', null, ''));

    const asked = requests.map((request) => new URL(request.url).search);
    expect(asked).toEqual(['?accountId=account-1&risk=1.5', '']);
  });

  it('creates a trade with the idea, the account and the risk exactly as typed', async () => {
    const requests = fakeApi({
      'POST /api/idea-trades': () => json({ id: 'trade-1' }, 201),
    });

    const id = await enterIdea({ ideaId: 'idea-1', accountId: 'account-1', risk: '1.50' });

    expect(id).toBe('trade-1');
    expect(await requests[0]!.text()).toBe(
      '{"ideaId":"idea-1","accountId":"account-1","risk":1.50}',
    );
  });

  it('leaves the account and the risk out of the entry when they are not given', async () => {
    const requests = fakeApi({
      'POST /api/idea-trades': () => json({ id: 'trade-1' }, 201),
    });

    await enterIdea({ ideaId: 'idea-1', accountId: null, risk: '' });

    expect(await requests[0]!.text()).toBe('{"ideaId":"idea-1"}');
  });

  it('cancels and closes a trade', async () => {
    const requests = fakeApi({
      'POST /api/idea-trades/trade-1/cancel': () => new Response(null, { status: 204 }),
      'POST /api/idea-trades/trade-1/close': () => new Response(null, { status: 204 }),
    });

    await cancelTrade('trade-1');
    await closeTrade('trade-1');

    expect(requests.map((request) => new URL(request.url).pathname)).toEqual([
      '/api/idea-trades/trade-1/cancel',
      '/api/idea-trades/trade-1/close',
    ]);
  });

  it('saves the settings with the numbers as typed', async () => {
    const requests = fakeApi({
      'PUT /api/idea-trading/settings': () => new Response(null, { status: 204 }),
    });

    await saveSettings({
      accountId: 'account-1',
      risk: '2.5',
      maxAmount: '300',
      maxOpenTrades: '3',
      maxTotalRisk: '6',
      minRiskReward: '1.2',
    });

    expect(await requests[0]!.text()).toBe(
      '{"accountId":"account-1","risk":2.5,"maxAmount":300,"maxOpenTrades":3,"maxTotalRisk":6,"minRiskReward":1.2}',
    );
  });
});
