import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { draftKey, drafts } from '@/modules/graph-editor';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { emaShortSummary, emaSummary, ladderSummary } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const definition = { formatVersion: 1, direction: 'long' as const, nodes: [] };

describe('strategy list', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('shows name, direction and categories of every strategy', async () => {
    fakeApi({ 'GET /api/strategies': () => json([emaSummary, emaShortSummary]) });

    renderApp('/strategies');

    const row = (await screen.findByRole('link', { name: 'EMA Short' })).closest('tr')!;
    expect(within(row).getByText('Шорт')).toBeInTheDocument();
    expect(within(row).getByText('linear')).toBeInTheDocument();
  });

  // Scenario "Пометка черновика".
  it('marks a strategy that has a draft in this browser', async () => {
    drafts.save(
      draftKey({ strategy: ladderSummary.id }),
      { name: 'Ladder', definition },
      new Date(),
    );
    fakeApi({ 'GET /api/strategies': () => json([emaSummary, ladderSummary]) });

    renderApp('/strategies');

    const ladder = (await screen.findByRole('link', { name: 'Ladder' })).closest('tr')!;
    const ema = screen.getByRole('link', { name: 'EMA' }).closest('tr')!;
    expect(within(ladder).getByText('черновик в браузере')).toBeInTheDocument();
    expect(within(ema).queryByText('черновик в браузере')).not.toBeInTheDocument();
  });

  // Scenario "Новая стратегия до сохранения": the list keeps the draft of a strategy the server does not have.
  it('lists the draft of a new strategy and opens it in the editor', async () => {
    drafts.save(draftKey({ newStrategy: 'd1' }), { name: 'Моя', definition }, new Date());
    fakeApi({ 'GET /api/strategies': () => json([]) });

    const { history } = renderApp('/strategies');

    const link = await screen.findByRole('link', { name: 'Моя' });
    expect(within(link.closest('tr')!).getByText('не сохранена')).toBeInTheDocument();
    await userEvent.click(link);
    await waitFor(() => expect(history.location.pathname).toBe('/strategies/new/d1'));
  });

  it('starts a new strategy in the editor', async () => {
    fakeApi({ 'GET /api/strategies': () => json([]) });

    const { history } = renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: '+ Новая стратегия' }));
    await waitFor(() =>
      expect(history.location.pathname).toMatch(/^\/strategies\/new\/[0-9a-f-]{36}$/),
    );
  });
});

describe('strategy list actions', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  async function openMenu(name: string) {
    const row = (await screen.findByRole('link', { name })).closest('tr')!;
    await userEvent.click(within(row).getByRole('button', { name: `Действия: ${name}` }));
  }

  // Scenario "Удаление стратегии с ботами".
  it('keeps a strategy that has bots and names them', async () => {
    fakeApi({
      'GET /api/strategies': () => json([emaSummary]),
      [`DELETE /api/strategies/${emaSummary.id}`]: () =>
        problem(409, 'Bots of the strategy stand in the way.', {
          usages: [],
          bots: [{ id: 'b1', name: 'EMA BTC', errors: {} }],
        }),
    });
    renderApp('/strategies');

    await openMenu('EMA');
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить стратегию' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Удалить нельзя');
    expect(alert).toHaveTextContent('EMA BTC');
    expect(screen.getByRole('link', { name: 'EMA' })).toBeInTheDocument();
  });

  it('deletes a strategy without bots', async () => {
    let list = [emaSummary, ladderSummary];
    fakeApi({
      'GET /api/strategies': () => json(list),
      [`DELETE /api/strategies/${emaSummary.id}`]: () => {
        list = [ladderSummary];

        return new Response(null, { status: 204 });
      },
    });
    renderApp('/strategies');

    await openMenu('EMA');
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить стратегию' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    await waitFor(() =>
      expect(screen.queryByRole('link', { name: 'EMA' })).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('link', { name: 'Ladder' })).toBeInTheDocument();
  });

  it('renames a strategy keeping its definition', async () => {
    const saved = { ...emaSummary, definition, version: 1, createdAt: '', updatedAt: '' };
    const requests = fakeApi({
      'GET /api/strategies': () => json([emaSummary]),
      [`GET /api/strategies/${emaSummary.id}`]: () => json(saved),
      [`PUT /api/strategies/${emaSummary.id}`]: () => new Response(null, { status: 204 }),
    });
    renderApp('/strategies');

    await openMenu('EMA');
    await userEvent.click(screen.getByRole('menuitem', { name: 'Переименовать' }));
    const name = within(screen.getByRole('dialog')).getByRole('textbox', { name: 'Название' });
    await userEvent.clear(name);
    await userEvent.type(name, 'EMA 2');
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Сохранить' }),
    );

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    const put = requests.find((request) => request.method === 'PUT')!;
    expect(await put.json()).toEqual({ name: 'EMA 2', definition });
  });
});
