import { fireEvent, screen } from '@testing-library/react';

import ema from '../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import catalog from './catalog.fixture.json';
import { fakeApi, type FakeRoute, json } from './fakeApi';
import { emaSummary, savedStrategy, trendFilter, trendFilterDefinition } from './fixtures';
import { renderApp } from './renderApp';

export const emaDefinition = ema;

/** Opens the editor of the EMA reference strategy; checks find no errors unless `routes` say otherwise. */
export function openEma(routes: Record<string, FakeRoute> = {}) {
  const requests = fakeApi({
    [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, ema)),
    'GET /api/strategy-nodes': () => json([...catalog, trendFilter]),
    [`POST /api/strategies/${emaSummary.id}/validate`]: () =>
      json({ errors: {}, usages: [], bots: [] }),
    ...routes,
  });

  return { requests, ...renderApp(`/strategies/${emaSummary.id}`) };
}

/** Selects a node and returns its card. A click without mousedown: dragging needs a view jsdom events lack. */
export async function selectNode(title: string) {
  fireEvent.click(await screen.findByRole('group', { name: title }));

  return screen.getByRole('region', { name: `Узел: ${title}` });
}

/** Opens the editor of the custom node "Фильтр тренда"; checks find no errors unless `routes` say otherwise. */
export function openTrendFilter(routes: Record<string, FakeRoute> = {}) {
  const requests = fakeApi({
    [`GET /api/strategy-nodes/${trendFilter.id}`]: () =>
      json({ ...trendFilter, definition: trendFilterDefinition }),
    'GET /api/strategy-nodes': () => json([...catalog, trendFilter]),
    [`POST /api/strategy-nodes/${trendFilter.id}/validate`]: () =>
      json({ errors: {}, usages: [], bots: [] }),
    ...routes,
  });

  return { requests, ...renderApp(`/nodes/${trendFilter.id}`) };
}
