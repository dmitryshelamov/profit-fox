import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

describe('the section «Сигналы»', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('opens on the root path', async () => {
    fakeApi(baseRoutes());
    const { history } = renderApp('/');

    expect(await screen.findByRole('heading', { name: 'Сигналы' })).toBeInTheDocument();
    expect(history.location.pathname).toBe('/signals');
  });

  // Scenarios «Сигнал в списке» and «Испорченный файл».
  it('lists the signals in the order the API gives and a broken file with its error', async () => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () =>
        json([
          { name: 'broken', description: null, error: 'graph is required' },
          { name: 'candle-drop', description: 'свеча', error: null },
          { name: 'funding-high', description: null, error: null },
        ]),
    });
    renderApp('/signals');

    const list = await screen.findByRole('region', { name: 'Сохранённые сигналы' });
    await waitFor(() => expect(within(list).getAllByRole('listitem')).toHaveLength(3));
    expect(
      within(list)
        .getAllByRole('listitem')
        .map((item) => item.getAttribute('aria-label')),
    ).toEqual(['Сигнал broken', 'Сигнал candle-drop', 'Сигнал funding-high']);
    expect(within(list).getByText(/Файл не читается: graph is required/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /H0001 funding-short-4h/ })).toHaveAttribute(
      'href',
      '/hypotheses/H0001',
    );
  });

  // Scenario «Удаление».
  it('deletes a signal only after the confirmation', async () => {
    const requests = fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json([{ name: 'price-drop', description: null, error: null }]),
      'DELETE /api/signals/price-drop': () => new Response(null, { status: 204 }),
    });
    const confirm = vi
      .spyOn(window, 'confirm')
      .mockReturnValueOnce(false)
      .mockReturnValueOnce(true);
    renderApp('/signals');

    const row = await screen.findByRole('listitem', { name: 'Сигнал price-drop' });
    await userEvent.click(within(row).getByRole('button', { name: 'Удалить' }));
    expect(requests.some((request) => request.method === 'DELETE')).toBe(false);
    await userEvent.click(within(row).getByRole('button', { name: 'Удалить' }));

    await waitFor(() =>
      expect(requests.filter((request) => request.method === 'DELETE')).toHaveLength(1),
    );
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  // Scenario «Занятое имя».
  it('shows a taken name on rename', async () => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json([{ name: 'price-drop', description: null, error: null }]),
      'POST /api/signals/price-drop/rename': () =>
        json(
          {
            errors: [
              { code: 'NameTaken', reason: 'the name funding-high is taken', field: 'name' },
            ],
          },
          409,
        ),
    });
    renderApp('/signals');

    const row = await screen.findByRole('listitem', { name: 'Сигнал price-drop' });
    await userEvent.click(within(row).getByRole('button', { name: 'Переименовать' }));
    const field = within(row).getByRole('textbox', { name: 'Новое имя' });
    await userEvent.clear(field);
    await userEvent.type(field, 'funding-high');
    await userEvent.click(within(row).getAllByRole('button', { name: 'Переименовать' }).at(-1)!);

    expect(await within(row).findByText('the name funding-high is taken')).toBeInTheDocument();
  });

  it('copies a signal under a new name with its graph', async () => {
    const source = {
      name: 'price-drop',
      description: 'd',
      graph: { formatVersion: 1, nodes: [] },
      layout: {},
    };
    const requests = fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json([{ name: 'price-drop', description: 'd', error: null }]),
      'GET /api/signals/price-drop': () => json(source),
      'POST /api/signals': () => json({ ...source, name: 'price-drop-copy' }, 201),
    });
    renderApp('/signals');

    const row = await screen.findByRole('listitem', { name: 'Сигнал price-drop' });
    await userEvent.click(within(row).getByRole('button', { name: 'Копировать' }));
    await userEvent.click(within(row).getByRole('button', { name: 'Скопировать' }));

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'POST', '/api/signals')).toEqual([
        { ...source, name: 'price-drop-copy' },
      ]),
    );
  });
});
