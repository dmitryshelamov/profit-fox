import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import catalog from '@/test/catalog.fixture.json';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { trendFilter } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

describe('composite node list', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows custom and built-in composite nodes apart, with their pins', async () => {
    fakeApi({ 'GET /api/strategy-nodes': () => json([...catalog, trendFilter]) });

    renderApp('/nodes');

    const custom = await screen.findByRole('region', { name: 'Мои' });
    const builtIn = screen.getByRole('region', { name: 'Встроенные' });
    expect(within(custom).getByRole('link', { name: 'Фильтр тренда' })).toBeInTheDocument();
    expect(within(custom).getByText('aboveTrend')).toBeInTheDocument();
    expect(
      within(builtIn)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Пересечение EMA', 'Ladder', 'DCA']);
    expect(within(builtIn).queryByRole('button', { name: /Удалить/ })).not.toBeInTheDocument();
    expect(screen.queryByText('Свеча')).not.toBeInTheDocument();
  });

  // Scenario "Удаление используемого узла".
  it('keeps a node that a strategy uses and names the strategy', async () => {
    fakeApi({
      'GET /api/strategy-nodes': () => json([...catalog, trendFilter]),
      [`DELETE /api/strategy-nodes/${trendFilter.id}`]: () =>
        problem(409, 'Graphs that use the node would break.', {
          usages: [{ kind: 'strategy', id: 's', name: 'EMA trend', errors: {} }],
        }),
    });
    renderApp('/nodes');

    await userEvent.click(await screen.findByRole('button', { name: 'Удалить: Фильтр тренда' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Удалить нельзя');
    expect(alert).toHaveTextContent('EMA trend');
    expect(screen.getByRole('link', { name: 'Фильтр тренда' })).toBeInTheDocument();
  });

  // Scenario "Клон встроенного узла".
  it('clones a built-in node into the custom nodes, ready to edit', async () => {
    const clone = {
      ...catalog.find((node) => node.id === 'ladder')!,
      id: '55555555-5555-4555-8555-555555555555',
      title: 'Ladder (копия)',
      group: 'custom',
    };
    let nodes: unknown[] = catalog;
    fakeApi({
      'GET /api/strategy-nodes': () => json(nodes),
      'POST /api/strategy-nodes/ladder/clone': () => {
        nodes = [...catalog, clone];

        return json({ id: clone.id }, 201);
      },
    });
    const { history } = renderApp('/nodes');

    await userEvent.click(await screen.findByRole('button', { name: 'Клонировать: Ladder' }));

    const custom = screen.getByRole('region', { name: 'Мои' });
    const link = await within(custom).findByRole('link', { name: 'Ladder (копия)' });
    await userEvent.click(link);
    await waitFor(() => expect(history.location.pathname).toBe(`/nodes/${clone.id}`));
  });
});
