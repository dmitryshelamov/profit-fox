import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { draftKey, drafts } from '@/modules/graph-editor';
import catalog from '@/test/catalog.fixture.json';
import { fakeApi, json } from '@/test/fakeApi';
import { emaSummary, savedStrategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const key = draftKey({ newStrategy: 'd1' });

function open() {
  const requests = fakeApi({
    'GET /api/strategy-nodes': () => json(catalog),
    'POST /api/strategies/validate': () => json({ errors: {}, usages: [], bots: [] }),
    'POST /api/strategies': () => json({ id: emaSummary.id }, 201),
    [`GET /api/strategies/${emaSummary.id}`]: async () =>
      json(
        savedStrategy(emaSummary, {
          formatVersion: 1,
          direction: 'long',
          parameters: [],
          nodes: [],
          wires: [],
        }),
      ),
  });

  return { requests, ...renderApp('/strategies/new/d1') };
}

async function nameIt(name: string) {
  await userEvent.click(await screen.findByRole('button', { name: 'Параметры' }));
  await userEvent.type(
    within(screen.getByRole('region', { name: 'Стратегия' })).getByRole('textbox', {
      name: 'Название',
    }),
    name,
  );
}

describe('new strategy', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('starts with the parameters node and lives only as a draft', async () => {
    const { requests } = open();

    expect(await screen.findByRole('group', { name: 'Параметры' })).toBeInTheDocument();
    await nameIt('Моя');

    await waitFor(() => expect(drafts.read(key)?.name).toBe('Моя'));
    expect(
      requests.some(
        (request) =>
          request.method === 'POST' && new URL(request.url).pathname === '/api/strategies',
      ),
    ).toBe(false);
  });

  it('creates the strategy on the first save and opens it', async () => {
    const { requests, history } = open();

    await screen.findByRole('group', { name: 'Параметры' });
    await nameIt('Моя');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/strategies/${emaSummary.id}`));
    const create = requests.find(
      (request) => request.method === 'POST' && new URL(request.url).pathname === '/api/strategies',
    )!;
    expect(await create.json()).toMatchObject({
      name: 'Моя',
      definition: { direction: 'long', nodes: [{ id: 'params', kind: 'parameters' }] },
    });
    expect(drafts.read(key)).toBeUndefined();
  });

  it('opens the draft of a new strategy as it was left', async () => {
    drafts.save(
      key,
      {
        name: 'Моя',
        definition: {
          formatVersion: 1,
          direction: 'short',
          parameters: [],
          nodes: [{ id: 'candle1', kind: 'candle' }],
          wires: [],
        },
      },
      new Date(),
    );
    open();

    expect(await screen.findByRole('group', { name: 'Свеча' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('banner', { name: 'Стратегия' })).toHaveTextContent('Моя');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled());
  });
});
