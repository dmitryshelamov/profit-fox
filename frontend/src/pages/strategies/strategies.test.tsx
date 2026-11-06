import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { draftKey, drafts } from '@/modules/graph-editor';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { emaShortSummary, emaSummary, ladderSummary } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';
import templates from '@/test/templates.fixture.json';

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
    fakeApi({
      'GET /api/strategies': () => json([]),
      'GET /api/strategy-templates': () => json([]),
    });

    const { history } = renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: '+ Новая стратегия' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Пустая стратегия' }));
    await waitFor(() =>
      expect(history.location.pathname).toMatch(/^\/strategies\/new\/[0-9a-f-]{36}$/),
    );
  });

  // Served over plain HTTP, as on the server, the page is not a secure context and has no crypto.randomUUID.
  it('starts a new strategy on a page that is not a secure context', async () => {
    const { getRandomValues } = crypto;
    vi.stubGlobal('crypto', { getRandomValues: getRandomValues.bind(crypto) });
    fakeApi({
      'GET /api/strategies': () => json([]),
      'GET /api/strategy-templates': () => json([]),
    });

    const { history } = renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: '+ Новая стратегия' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Пустая стратегия' }));
    await waitFor(() =>
      expect(history.location.pathname).toMatch(/^\/strategies\/new\/[0-9a-f-]{36}$/),
    );
  });
});

describe('new strategy dialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  function open(routes: Parameters<typeof fakeApi>[0] = {}) {
    const requests = fakeApi({
      'GET /api/strategies': () => json([]),
      'GET /api/strategy-templates': () => json(templates.list),
      'GET /api/strategy-templates/rsi-reversion': () => json(templates.details['rsi-reversion']),
      'GET /api/strategy-templates/ladder-plus': () => json(templates.details['ladder-plus']),
      ...routes,
    });

    return { requests, ...renderApp('/strategies') };
  }

  async function openDialog() {
    await userEvent.click(await screen.findByRole('button', { name: '+ Новая стратегия' }));

    return screen.findByRole('dialog', { name: 'Новая стратегия' });
  }

  // Scenario "Выбор при создании".
  it('offers an empty strategy and every template with its name, idea and direction', async () => {
    open();

    const dialog = await openDialog();

    expect(await within(dialog).findByRole('button', { name: 'Пустая стратегия' })).toBeVisible();
    expect(templates.list).toHaveLength(14);

    for (const template of templates.list) {
      const card = within(dialog).getByText(template.name, { exact: true }).closest('button')!;
      expect(card).toHaveTextContent(template.description);
      expect(card).toHaveTextContent(template.direction === 'short' ? 'Шорт' : 'Лонг');
    }
  });

  // Scenario "Шаблоны недоступны".
  it('says the templates did not load and still offers an empty strategy', async () => {
    const { history } = open({ 'GET /api/strategy-templates': () => problem(500, 'boom') });

    const dialog = await openDialog();

    expect(await within(dialog).findByText('Шаблоны не загрузились.')).toBeVisible();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Пустая стратегия' }));
    await waitFor(() =>
      expect(history.location.pathname).toMatch(/^\/strategies\/new\/[0-9a-f-]{36}$/),
    );
  });

  // Scenario "Стратегия из шаблона": the draft of the new strategy holds the name and the definition of the template.
  it('opens a draft with the name and the definition of the chosen template', async () => {
    const { history } = open();

    const dialog = await openDialog();
    await userEvent.click(await within(dialog).findByRole('button', { name: /^Отскок RSI/ }));

    await waitFor(() =>
      expect(history.location.pathname).toMatch(/^\/strategies\/new\/[0-9a-f-]{36}$/),
    );
    const draftId = history.location.pathname.split('/').pop()!;
    expect(drafts.read(draftKey({ newStrategy: draftId }))).toMatchObject({
      name: 'Отскок RSI',
      definition: templates.details['rsi-reversion'].definition,
    });
  });

  it('stays in the dialog with every choice open when the chosen template does not load', async () => {
    const { history } = open({
      'GET /api/strategy-templates/rsi-reversion': () => problem(500, 'boom'),
    });

    const dialog = await openDialog();
    const card = await within(dialog).findByRole('button', { name: /^Отскок RSI/ });
    await userEvent.click(card);

    await waitFor(() => expect(card).toBeEnabled());
    expect(within(dialog).getByRole('button', { name: 'Пустая стратегия' })).toBeEnabled();
    expect(history.location.pathname).toBe('/strategies');
  });

  // Scenario "Шаблон до сохранения": the template is a draft in the browser until the first save.
  it('keeps the chosen template as an unsaved draft the server does not have', async () => {
    const { requests, history } = open();
    const dialog = await openDialog();
    await userEvent.click(await within(dialog).findByRole('button', { name: /^Ladder\+/ }));
    await waitFor(() => expect(history.location.pathname).toMatch(/^\/strategies\/new\//));

    await history.push('/strategies');

    const link = await screen.findByRole('link', { name: 'Ladder+' });
    expect(within(link.closest('tr')!).getByText('не сохранена')).toBeInTheDocument();
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
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

describe('strategy list: bot creation', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Создание из списка стратегий".
  it('opens the new bot form with the strategy', async () => {
    fakeApi({
      'GET /api/strategies': () => json([emaSummary, emaShortSummary]),
      [`GET /api/strategies/${emaShortSummary.id}`]: () =>
        json({ ...emaShortSummary, definition: { formatVersion: 1, nodes: [] } }),
    });
    const { history } = renderApp('/strategies');

    const row = (await screen.findByRole('link', { name: 'EMA Short' })).closest('tr')!;
    await userEvent.click(within(row).getByRole('button', { name: 'Действия: EMA Short' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Создать бота' }));

    await waitFor(() => expect(history.location.pathname).toBe('/bots/new'));
    expect(await screen.findByLabelText('Стратегия')).toHaveValue(emaShortSummary.id);
  });
});
