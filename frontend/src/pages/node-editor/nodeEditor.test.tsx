import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { problem } from '@/test/fakeApi';
import { trendFilter } from '@/test/fixtures';
import { openTrendFilter, selectNode } from '@/test/openEditor';

describe('custom composite node editor', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('gives the inputs node an output for a new declared input', async () => {
    openTrendFilter();

    await userEvent.click(await screen.findByRole('button', { name: 'Входы и выходы' }));
    const card = screen.getByRole('region', { name: 'Составной узел' });
    await userEvent.click(within(card).getByRole('button', { name: '+ Добавить вход' }));
    const name = within(card).getAllByRole('textbox', { name: 'Имя входа' }).at(-1)!;
    await userEvent.clear(name);
    await userEvent.type(name, 'period');
    await userEvent.tab();

    await waitFor(() =>
      expect(
        within(screen.getByRole('group', { name: 'Входы узла' })).getByText('period'),
      ).toBeInTheDocument(),
    );
  });

  it('saves the node through the node endpoints', async () => {
    const { requests } = openTrendFilter({
      [`PUT /api/strategy-nodes/${trendFilter.id}`]: () => new Response(null, { status: 204 }),
    });

    const card = await selectNode('EMA');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    expect(
      requests.some(
        (request) =>
          new URL(request.url).pathname === `/api/strategy-nodes/${trendFilter.id}/validate`,
      ),
    ).toBe(true);
  });

  // Scenario "Ломающая правка составного узла".
  it('names the graphs a breaking edit would break and keeps the edit', async () => {
    openTrendFilter({
      [`PUT /api/strategy-nodes/${trendFilter.id}`]: () =>
        problem(409, 'Graphs that use the node would break.', {
          usages: [
            {
              kind: 'strategy',
              id: 's',
              name: 'EMA trend',
              errors: { 'graph.trend.period': 'Node has no input period.' },
            },
          ],
        }),
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Входы и выходы' }));
    const card = screen.getByRole('region', { name: 'Составной узел' });
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить вход source' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    const dialog = await screen.findByRole('dialog', { name: 'Правка не сохранена' });
    expect(dialog).toHaveTextContent('EMA trend');
    expect(dialog).toHaveTextContent('graph.trend.period');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Вернуться к правке' }));
    expect(
      within(screen.getByRole('region', { name: 'Составной узел' })).queryByRole('textbox', {
        name: 'Имя входа',
      }),
    ).not.toBeInTheDocument();
  });
});
