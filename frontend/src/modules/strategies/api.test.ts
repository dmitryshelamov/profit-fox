import { QueryClient } from '@tanstack/react-query';

import { fakeApi, json } from '@/test/fakeApi';
import templates from '@/test/templates.fixture.json';

import { fetchStrategyTemplate, strategyTemplatesQuery } from './api';

describe('strategy templates', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Список шаблонов".
  it('reads the list of templates', async () => {
    const requests = fakeApi({ 'GET /api/strategy-templates': () => json(templates.list) });

    const list = await new QueryClient().fetchQuery(strategyTemplatesQuery);

    expect(list).toHaveLength(14);
    expect(list[0]).toMatchObject({ code: 'ema-crossover', name: 'EMA Crossover' });
    expect(requests).toHaveLength(1);
  });

  // Scenario "Чтение шаблона".
  it('reads the definition of a template by its code', async () => {
    const requests = fakeApi({
      'GET /api/strategy-templates/rsi-reversion': () => json(templates.details['rsi-reversion']),
    });

    const template = await fetchStrategyTemplate('rsi-reversion');

    expect(template.name).toBe('Отскок RSI');
    expect(template.definition).toMatchObject({ direction: 'long' });
    expect(new URL(requests[0]!.url).pathname).toBe('/api/strategy-templates/rsi-reversion');
  });
});
