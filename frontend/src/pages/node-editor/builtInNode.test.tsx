import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import catalog from '@/test/catalog.fixture.json';
import emaCross from '@/test/emaCross.fixture.json';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { selectNode } from '@/test/openEditor';
import { renderApp } from '@/test/renderApp';

const cloneId = '66666666-6666-4666-8666-666666666666';

function open() {
  const requests = fakeApi({
    'GET /api/strategy-nodes/ema-cross': () => json(emaCross),
    'GET /api/strategy-nodes': () => json(catalog),
    'POST /api/strategy-nodes/ema-cross/clone': () => json({ id: cloneId }, 201),
    [`GET /api/strategy-nodes/${cloneId}`]: () =>
      json({ ...emaCross, id: cloneId, title: 'Пересечение EMA (копия)', group: 'custom' }),
    [`POST /api/strategy-nodes/${cloneId}/validate`]: () =>
      json({ errors: {}, usages: [], bots: [] }),
  });

  return { requests, ...renderApp('/nodes/ema-cross') };
}

describe('built-in composite node', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('opens read only', async () => {
    open();

    const card = await selectNode('EMA');

    expect(screen.getByRole('banner', { name: 'Составной узел' })).toHaveTextContent(
      'Только чтение',
    );
    expect(screen.queryByRole('button', { name: 'Добавить узел' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Выстроить' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Сохранить' })).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Удалить узел' })).not.toBeInTheDocument();
    expect(within(card).getByRole('combobox', { name: 'Источник source' })).toBeDisabled();
  });

  // Scenario "Клонировать и править".
  it('clones the node and opens the clone to edit', async () => {
    const { history } = open();

    await userEvent.click(await screen.findByRole('button', { name: 'Клонировать и править' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/nodes/${cloneId}`));
    expect(await screen.findByRole('button', { name: 'Сохранить' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Добавить узел' })).toBeInTheDocument();
  });

  it('says so when there is no such composite node', async () => {
    fakeApi({
      'GET /api/strategy-nodes': () => json(catalog),
      'GET /api/strategy-nodes/nope': () => problem(404, 'There is no node nope.'),
    });

    renderApp('/nodes/nope');

    expect(await screen.findByText('Составной узел не найден.')).toBeInTheDocument();
  });
});
