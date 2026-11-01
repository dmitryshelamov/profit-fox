import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import emaCross from '@/test/emaCross.fixture.json';
import { json } from '@/test/fakeApi';
import { openEma, selectNode } from '@/test/openEditor';

describe('composite node of a strategy', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Открыть узел из стратегии".
  it('opens the built-in node a strategy uses, read only', async () => {
    const { history } = openEma({ 'GET /api/strategy-nodes/ema-cross': () => json(emaCross) });

    const card = await selectNode('Пересечение EMA');
    await userEvent.click(within(card).getByRole('link', { name: 'Открыть узел' }));

    await waitFor(() => expect(history.location.pathname).toBe('/nodes/ema-cross'));
    expect(await screen.findByRole('note')).toHaveTextContent('Встроенный узел нельзя править');
  });

  it('offers no node to open for a simple node', async () => {
    openEma();

    const card = await selectNode('Свеча');

    expect(within(card).queryByRole('link', { name: 'Открыть узел' })).not.toBeInTheDocument();
  });
});
