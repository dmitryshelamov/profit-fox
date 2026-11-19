import { fireEvent, screen, waitFor, within } from '@testing-library/react';

import { fakeApi, type FakeRoute, json } from './fakeApi';
import { renderApp } from './renderApp';
import { baseRoutes, savedFundingHigh } from './routes';

/** Opens the editor of the saved signal `funding-high` (the graph of H0001); `routes` add to or replace the base ones. */
export function openFundingHigh(routes: Record<string, FakeRoute> = {}) {
  const requests = fakeApi({
    ...baseRoutes(),
    'GET /api/signals/funding-high': () => json(savedFundingHigh()),
    'PUT /api/signals/funding-high': async (request) =>
      json({ name: 'funding-high', ...(await request.json()) }),
    ...routes,
  });
  return { requests, ...renderApp('/signals/funding-high') };
}

/** The node on the canvas with its title and id, once React Flow has drawn it. */
export function findNode(title: string, id: string) {
  return screen.findByRole('group', { name: `${title} ${id}` });
}

/** Selects a node and returns its card. A click without mousedown: dragging needs a view jsdom events lack. */
export async function selectNode(title: string, id: string) {
  fireEvent.click(await findNode(title, id));
  return screen.getByRole('region', { name: `Узел: ${title} ${id}` });
}

/** Waits until the last edit is checked: the status says it is not being checked. */
export async function checked() {
  await waitFor(
    () =>
      expect(screen.getByRole('status', { name: 'Состояние' }).textContent).not.toBe('проверяю…'),
    {
      timeout: 2000,
    },
  );
}

export function saveButton() {
  return screen.getByRole('button', { name: 'Сохранить' });
}

export function card(name: string) {
  return within(screen.getByRole('region', { name }));
}
