import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { strategyDraftKey } from '@/modules/graph-editor/drafts/drafts';
import { card, checked, findNode, saveButton, selectNode } from '@/test/editor';
import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes, savedFundingHigh } from '@/test/routes';
import { backtest } from '@/test/runs';
import {
  fundingHigh,
  fundingHighId,
  fundingShort,
  newStrategy,
  savedFundingShort,
  strategyRoutes,
} from '@/test/strategies';

function routes(extra: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({ ...baseRoutes(), ...strategyRoutes(), ...extra });
}

describe('the section «Стратегии»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Пустой список».
  it('says there are no strategies and offers a new one', async () => {
    routes();
    renderApp('/strategies');

    expect(await screen.findByText(/Стратегий нет/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Новая стратегия' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Стратегии' })).toBeInTheDocument();
  });

  // Scenario «Список».
  it('lists the strategies with their latest versions', async () => {
    routes({
      'GET /api/strategies': () =>
        json([
          { name: 'funding-short', description: null, version: 2, error: null },
          { name: 'price-drop-long', description: 'лонг', version: 1, error: null },
        ]),
    });
    renderApp('/strategies');

    const first = await screen.findByRole('row', { name: 'Стратегия funding-short' });
    expect(within(first).getByText('версия 2')).toBeInTheDocument();
    const second = screen.getByRole('row', { name: 'Стратегия price-drop-long' });
    expect(within(second).getByText('версия 1')).toBeInTheDocument();
  });

  // Spec strategy-constructor «Сигналы и выходы стратегии в списке»: «Сигнал и срок», «Стоп и цель», «Стратегия без
  // сигналов»; «Отметка новой версии сигнала в списке стратегий»: «Есть версия новее».
  it('shows the signals of the latest version, a newer version of a signal and the exits', async () => {
    const requests = routes({
      'GET /api/strategies': () =>
        json([
          {
            name: 'breakout-long',
            description: null,
            version: 7,
            error: null,
            signals: [],
            exits: ['stop-loss', 'take-profit'],
          },
          {
            name: 'breakout-trail',
            description: null,
            version: 1,
            error: null,
            signals: [{ name: 'volume-breakout', version: 3, latestVersion: 4 }],
            exits: ['close-after'],
          },
          {
            name: 'funding-short',
            description: null,
            version: 2,
            error: null,
            signals: [{ name: 'funding-high', version: 1, latestVersion: 1 }],
            exits: ['close-after'],
          },
          { name: 'plain', description: null, version: 1, error: null, signals: [], exits: [] },
        ]),
    });
    renderApp('/strategies');

    const funding = within(await screen.findByRole('row', { name: 'Стратегия funding-short' }));
    expect(funding.getByRole('link', { name: 'funding-high v1' })).toHaveAttribute(
      'href',
      '/signals/funding-high',
    );
    expect(funding.getByText('срок')).toBeInTheDocument();
    expect(funding.queryByText(/^есть v/)).toBeNull();

    const trail = within(screen.getByRole('row', { name: 'Стратегия breakout-trail' }));
    expect(trail.getByRole('link', { name: 'volume-breakout v3' })).toBeInTheDocument();
    expect(trail.getByText('есть v4')).toHaveAttribute('data-tone', 'warn');

    const long = within(screen.getByRole('row', { name: 'Стратегия breakout-long' }));
    expect(long.getByText('стоп · цель')).toBeInTheDocument();
    expect(long.getAllByText('—')).toHaveLength(1);

    expect(
      within(screen.getByRole('row', { name: 'Стратегия plain' })).getAllByText('—'),
    ).toHaveLength(2);
    expect(
      screen
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent)
        .slice(0, 7),
    ).toEqual(['Имя', 'Описание', 'Версия', 'Вид', 'Сигналы', 'Выход', 'Последний прогон']);
    // The list only shows the newer version: nothing is written.
    expect(requests.every((request) => request.method === 'GET')).toBe(true);
  });

  // Spec bench-ui «Поиск в списке».
  it('searches the strategies by a part of the name', async () => {
    routes({
      'GET /api/strategies': () =>
        json([
          { name: 'breakout-long', description: null, version: 7, error: null },
          { name: 'funding-short', description: null, version: 2, error: null },
        ]),
    });
    renderApp('/strategies');
    await screen.findByRole('row', { name: 'Стратегия funding-short' });

    await userEvent.type(screen.getByRole('searchbox', { name: 'Найти по имени' }), 'Break');

    expect(
      screen
        .getAllByRole('row', { name: /^Стратегия / })
        .map((row) => row.getAttribute('aria-label')),
    ).toEqual(['Стратегия breakout-long']);
  });

  // Scenario «Занятое имя».
  it('shows a taken name on rename', async () => {
    routes({
      'GET /api/strategies': () =>
        json([{ name: 'price-drop-long', description: null, version: 1, error: null }]),
      'POST /api/strategies/price-drop-long/rename': () =>
        json(
          {
            errors: [
              { code: 'NameTaken', reason: 'the name funding-short is taken', field: 'name' },
            ],
          },
          409,
        ),
    });
    renderApp('/strategies');

    const row = await screen.findByRole('row', { name: 'Стратегия price-drop-long' });
    await userEvent.click(within(row).getByRole('button', { name: 'Действия price-drop-long' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Переименовать' }));
    const dialog = screen.getByRole('dialog', { name: 'Переименовать стратегию price-drop-long' });
    const field = within(dialog).getByRole('textbox', { name: 'Новое имя' });
    await userEvent.clear(field);
    await userEvent.type(field, 'funding-short');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Переименовать' }));

    expect(await within(dialog).findByText('the name funding-short is taken')).toBeInTheDocument();
    expect(dialog).toBeVisible();
  });

  // Scenario «Удаление».
  it('deletes a strategy after the confirmation', async () => {
    let deleted = false;
    const requests = routes({
      'GET /api/strategies': () =>
        json(
          deleted ? [] : [{ name: 'funding-short', description: null, version: 1, error: null }],
        ),
      'DELETE /api/strategies/funding-short': () => {
        deleted = true;
        return new Response(null, { status: 204 });
      },
    });
    renderApp('/strategies');

    const row = await screen.findByRole('row', { name: 'Стратегия funding-short' });
    await userEvent.click(within(row).getByRole('button', { name: 'Действия funding-short' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить' }));
    const question = screen.getByRole('dialog', { name: 'Удалить стратегию funding-short' });
    expect(question).toHaveTextContent('Удалить стратегию funding-short со всеми версиями?');
    await userEvent.click(within(question).getByRole('button', { name: 'Удалить' }));

    expect(await screen.findByText(/Стратегий нет/)).toBeInTheDocument();
    expect(requests.filter((request) => request.method === 'DELETE')).toHaveLength(1);
  });

  // Scenario «Новая стратегия»; spec strategy-constructor «Вид при создании стратегии».
  it('asks for the kind and opens a new strategy of ideas of an enter and a close-after that is not in the list', async () => {
    const requests = routes();
    renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: 'Новая стратегия' }));
    const dialog = screen.getByRole('dialog', { name: 'Вид новой стратегии' });
    expect(within(dialog).getByText(/случайным входом/)).toBeInTheDocument();
    expect(within(dialog).getByText(/лесенки/)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: /категория идей/ }));

    expect(await findNode('Вход', 'enter')).toBeInTheDocument();
    expect(await findNode('Закрыть через', 'close-after')).toBeInTheDocument();
    expect(screen.getByLabelText('Вид')).toHaveTextContent('категория идей');
    await checked();
    expect(
      requests.some(
        (request) =>
          request.method === 'POST' && new URL(request.url).pathname === '/api/strategies',
      ),
    ).toBe(false);
    const checks = (await bodiesOf(requests, 'POST', '/api/strategies/validate')) as {
      kind?: string;
    }[];
    expect(checks.at(-1)!.kind).toBe('idea');
  });

  // Scenario «Выбор вида».
  it('opens a new strategy of a bot of the position alone, with its kind in the plaque and in the draft', async () => {
    const requests = routes();
    const { history } = renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: 'Новая стратегия' }));
    await userEvent.click(
      within(screen.getByRole('dialog', { name: 'Вид новой стратегии' })).getByRole('button', {
        name: /для бота/,
      }),
    );

    expect(await findNode('Позиция', 'position')).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Вход enter' })).toBeNull();
    expect(screen.queryByRole('group', { name: 'Закрыть через close-after' })).toBeNull();
    expect(screen.getByLabelText('Вид')).toHaveTextContent('для бота');
    const draftId = history.location.pathname.split('/').at(-1)!;
    expect(JSON.parse(localStorage.getItem(strategyDraftKey({ newId: draftId }))!).kind).toBe(
      'bot',
    );
    expect(
      requests
        .filter((request) => new URL(request.url).pathname === '/api/nodes/strategy')
        .map((request) => new URL(request.url).searchParams.get('kind')),
    ).toEqual(['bot']);
  });

  // Scenario «Вид в строке списка».
  it('says the kind of each strategy in its row', async () => {
    routes({
      'GET /api/strategies': () =>
        json([
          { ...listItem('funding-short'), kind: 'idea' },
          { ...listItem('ladder'), kind: 'bot' },
        ]),
    });
    renderApp('/strategies');

    const funding = within(await screen.findByRole('row', { name: 'Стратегия funding-short' }));
    expect(funding.getByText('идеи')).toBeInTheDocument();
    const ladder = within(screen.getByRole('row', { name: 'Стратегия ladder' }));
    expect(ladder.getByText('бот')).toBeInTheDocument();
  });

  // Scenario «Копия лесенки».
  it('copies a strategy with its kind', async () => {
    const requests = routes({
      'GET /api/strategies': () => json([{ ...listItem('ladder'), kind: 'bot' }]),
      'GET /api/strategies/ladder': () =>
        json({ ...savedFundingShort(1), name: 'ladder', kind: 'bot' }),
      'POST /api/strategies': async (request) =>
        json({ ...(await request.json()), version: 1, signals: [] }, 201),
    });
    renderApp('/strategies');

    const row = await screen.findByRole('row', { name: 'Стратегия ladder' });
    await userEvent.click(within(row).getByRole('button', { name: 'Действия ladder' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Копировать' }));
    const dialog = screen.getByRole('dialog', { name: 'Копировать стратегию ladder' });
    const field = within(dialog).getByRole('textbox', { name: 'Имя копии' });
    await userEvent.clear(field);
    await userEvent.type(field, 'ladder-2');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Скопировать' }));

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'POST', '/api/strategies')).toHaveLength(1),
    );
    const [copy] = (await bodiesOf(requests, 'POST', '/api/strategies')) as {
      name: string;
      kind: string;
    }[];
    expect(copy).toMatchObject({ name: 'ladder-2', kind: 'bot' });
  });
});

/** A line of the list of strategies without signals, exits and runs. */
function listItem(name: string, version = 1) {
  return { name, description: null, version, error: null, signals: [], exits: [] };
}

describe('the kind of a strategy in the editor', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario «Лимитная заявка в категории идей».
  it('offers no market order, limit order and close of the position to a strategy of ideas', async () => {
    const requests = routes({
      'GET /api/strategies/funding-short': () => json(savedFundingShort(1)),
    });
    renderApp('/strategies/funding-short');
    await findNode('Вход', 'enter');

    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));

    const position = within(screen.getByRole('group', { name: 'Позиция' }));
    expect(position.getByRole('button', { name: 'Стоп' })).toBeInTheDocument();
    for (const title of ['Рыночная заявка', 'Лимитная заявка', 'Закрыть позицию']) {
      expect(position.queryByRole('button', { name: title })).toBeNull();
    }
    expect(screen.getByLabelText('Вид')).toHaveTextContent('категория идей');
    expect(
      requests
        .filter((request) => new URL(request.url).pathname === '/api/nodes/strategy')
        .map((request) => new URL(request.url).searchParams.get('kind')),
    ).toEqual(['idea']);
  });

  it('offers the orders and the close of the position to a strategy of a bot', async () => {
    routes();
    renderApp(newStrategy('bot'));
    await findNode('Позиция', 'position');

    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));

    const position = within(screen.getByRole('group', { name: 'Позиция' }));
    for (const title of ['Рыночная заявка', 'Лимитная заявка', 'Закрыть позицию']) {
      expect(position.getByRole('button', { name: title })).toBeInTheDocument();
    }
  });

  it('saves a new strategy with its kind and a saved one with the kind it has', async () => {
    const requests = routes({
      'GET /api/strategies/funding-short': () => json(savedFundingShort(1)),
      'PUT /api/strategies/funding-short': async (request) =>
        json({ ...savedFundingShort(1), version: 2, graph: (await request.json()).graph }),
    });
    renderApp('/strategies/funding-short');
    await findNode('Вход', 'enter');
    const description = screen.getByRole('textbox', { name: 'Описание' });
    await userEvent.type(description, 'шорт');
    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    await userEvent.click(saveButton());

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'PUT', '/api/strategies/funding-short')).toHaveLength(1),
    );
    const [saved] = (await bodiesOf(requests, 'PUT', '/api/strategies/funding-short')) as {
      kind: string;
    }[];
    expect(saved!.kind).toBe('idea');
  });
});

describe('the editor of a strategy', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario «Ошибка на узле».
  it('marks a stop-loss without a distance and lists the error', async () => {
    const graph = fundingShort();
    routes({
      'GET /api/strategies/funding-short': () =>
        json(
          savedFundingShort(1, {
            ...graph,
            nodes: [...graph.nodes, { id: 'stop', kind: 'stop-loss' }],
          }),
        ),
    });
    renderApp('/strategies/funding-short');

    const stop = await findNode('Стоп', 'stop');
    await waitFor(() => expect(stop).toHaveAttribute('aria-invalid', 'true'), { timeout: 2000 });
    await userEvent.click(screen.getByRole('button', { name: 'Ошибки 1' }));
    expect(card('Ошибки').getByText('stop.distance')).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
  });

  // Change add-position-node, task 12.2: an order and the position come by a wire only.
  it('shows no value field on the canvas at an input of an order or of the position', async () => {
    const graph = fundingShort();
    routes({
      'GET /api/strategies/funding-short': () =>
        json(
          savedFundingShort(1, {
            ...graph,
            nodes: [...graph.nodes, { id: 'position', kind: 'position' }],
          }),
        ),
    });
    renderApp('/strategies/funding-short');

    const position = await findNode('Позиция', 'position');
    const closeAfter = await findNode('Закрыть через', 'close-after');
    // By the label, not by the role: React Flow hides a node while it measures it, and a hidden field has no role.
    expect(within(closeAfter).getByLabelText('Значение close-after.after')).toHaveValue('4h');
    expect(within(closeAfter).getByText('position')).toBeInTheDocument();
    expect(within(closeAfter).queryByLabelText('Значение close-after.position')).toBeNull();
    expect(within(position).getByText('orders')).toBeInTheDocument();
    expect(within(position).queryByLabelText('Значение position.orders')).toBeNull();
  });

  // Spec strategy-constructor «Несколько проводов в позицию на холсте»: the card of the position lists its wires.
  it('takes one wire away from the orders of the position by its «Убрать»', async () => {
    routes();
    renderApp('/strategies/new/abc');

    const position = await selectNode('Позиция', 'position');
    const wires = () =>
      within(within(position).getByRole('list', { name: 'Провода orders' }))
        .getAllByRole('listitem')
        .map((item) => item.textContent);
    expect(wires()).toEqual(['enter.orderУбрать', 'close-after.orderУбрать']);

    await userEvent.click(within(position).getByRole('button', { name: 'Убрать enter.order' }));

    expect(wires()).toEqual(['close-after.orderУбрать']);
    // The removed order can be wired again, the other one is still wired.
    expect(
      within(within(position).getByRole('combobox', { name: 'Добавить orders' }))
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['добавить провод', 'enter.order']);
  });

  // Scenario «Сигналы в палитре».
  it('offers the saved signals in the palette', async () => {
    routes({
      'GET /api/signals': () =>
        json([
          { name: 'funding-high', description: null, version: 2, error: null, id: fundingHighId },
          { name: 'price-drop', description: null, version: 1, error: null, id: 'b1' },
          { name: 'broken', description: null, version: 0, error: 'does not read', id: null },
        ]),
    });
    renderApp('/strategies/new/abc');
    await findNode('Вход', 'enter');

    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));

    const signals = within(screen.getByRole('group', { name: 'Сигналы' }));
    expect(signals.getAllByRole('button').map((button) => button.textContent)).toEqual([
      'funding-high',
      'price-drop',
    ]);
  });

  // Scenarios «Добавление сигнала» and «Значение по умолчанию».
  it('adds a signal on its latest version with its pins and the value of its parameter', async () => {
    routes();
    renderApp('/strategies/new/abc');
    await findNode('Вход', 'enter');

    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));
    await userEvent.click(
      within(screen.getByRole('group', { name: 'Сигналы' })).getByRole('button', {
        name: 'funding-high',
      }),
    );

    const node = await findNode('funding-high', 'funding-high');
    expect(within(node).getByText('версия: 2')).toBeInTheDocument();
    expect(await within(node).findByText('threshold')).toBeInTheDocument();
    expect(within(node).getByText('event')).toBeInTheDocument();
    const value = within(node).getByRole('textbox', { name: 'Значение funding-high.threshold' });
    expect(value).toHaveValue('');
    expect(value).toHaveAttribute('placeholder', '0.001');
    await checked();
    expect(node).toHaveAttribute('aria-invalid', 'false');
  });

  // Scenario «Обновление ссылки».
  it('updates a node to the latest version of its signal, and the save keeps it', async () => {
    const requests = routes({
      'GET /api/strategies/funding-short': () => json(savedFundingShort(1)),
      'PUT /api/strategies/funding-short': async (request) =>
        json({ ...savedFundingShort(1), version: 2, graph: (await request.json()).graph }),
    });
    renderApp('/strategies/funding-short');

    const node = await findNode('funding-high', 's');
    expect(within(node).getByText('версия: 1, есть версия: 2')).toBeInTheDocument();
    const signal = within(await selectNode('funding-high', 's')).getByRole('region', {
      name: 'Сигнал узла',
    });
    expect(within(signal).getByRole('link', { name: 'Открыть сигнал' })).toHaveAttribute(
      'href',
      '/signals/funding-high',
    );
    await userEvent.click(within(signal).getByRole('button', { name: 'Обновить до версии 2' }));

    await waitFor(() => expect(within(node).getByText('версия: 2')).toBeInTheDocument());
    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    await userEvent.click(saveButton());

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'PUT', '/api/strategies/funding-short')).toHaveLength(1),
    );
    const [saved] = (await bodiesOf(requests, 'PUT', '/api/strategies/funding-short')) as {
      graph: { nodes: { id: string; settings?: Record<string, string> }[] };
    }[];
    expect(saved!.graph.nodes[0]!.settings).toEqual({ signal: fundingHighId, version: '2' });
    // The title of the editor and the card of the node both say version 2 now.
    await waitFor(() => expect(screen.getAllByText('версия 2')).toHaveLength(2));
  });

  // Change add-position-node, task 12.3; spec strategy-constructor «Сохранение поднятой стратегии». The API read the
  // stored graph of the former form raised and says so; the editor offers to save it as it is.
  it('opens a strategy read raised as not saved and saves the raised graph without an edit', async () => {
    const former = fundingShort();
    const raised = {
      ...former,
      nodes: [
        former.nodes[0]!,
        { ...former.nodes[1]!, inputs: { size: 100 } },
        former.nodes[2]!,
        { id: 'position', kind: 'position' },
      ],
      wires: [
        ...former.wires,
        { from: 'enter.order', to: 'position.orders' },
        { from: 'position.position', to: 'close-after.position' },
        { from: 'close-after.order', to: 'position.orders' },
      ],
    };
    const requests = routes({
      'GET /api/strategies/funding-short': () =>
        json({ ...savedFundingShort(1, raised), raised: true }),
      'PUT /api/strategies/funding-short': async (request) =>
        json({
          ...savedFundingShort(1, raised),
          version: 2,
          graph: (await request.json()).graph,
          raised: false,
        }),
    });
    renderApp('/strategies/funding-short');
    await findNode('Позиция', 'position');

    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    const state = screen.getByRole('status', { name: 'Состояние' });
    expect(state).toHaveTextContent('не сохранено');
    // Nothing was edited: there is nothing to discard and no draft to keep.
    expect(screen.getByRole('button', { name: 'Отбросить' })).toBeDisabled();
    await userEvent.click(saveButton());

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'PUT', '/api/strategies/funding-short')).toHaveLength(1),
    );
    const [saved] = (await bodiesOf(requests, 'PUT', '/api/strategies/funding-short')) as {
      graph: typeof raised;
    }[];
    expect(saved!.graph.nodes).toEqual(raised.nodes);
    expect(saved!.graph.wires).toEqual(raised.wires);
    await waitFor(() => expect(state).toHaveTextContent('сохранено'));
    expect(state).not.toHaveTextContent('не сохранено');
    expect(saveButton()).toBeDisabled();
    expect(screen.getByText('версия 2')).toBeInTheDocument();
  });

  it('opens a strategy stored in the new form as saved', async () => {
    routes({
      'GET /api/strategies/funding-short': () => json({ ...savedFundingShort(1), raised: false }),
    });
    renderApp('/strategies/funding-short');
    await findNode('Вход', 'enter');
    await checked();

    expect(screen.getByRole('status', { name: 'Состояние' })).toHaveTextContent(/^сохранено$/);
    expect(saveButton()).toBeDisabled();
  });

  // Scenario «Черновики с одним именем».
  it('offers the draft of the strategy, not that of the signal of the same name', async () => {
    const strategy = {
      ...fundingShort(),
      nodes: [
        ...fundingShort().nodes,
        { id: 'stop', kind: 'stop-loss', inputs: { distance: 0.02 } },
      ],
    };
    localStorage.setItem(
      'profit-fox:draft:strategy:funding-high',
      JSON.stringify({
        savedAt: '2026-11-21T10:00:00Z',
        description: 'черновик стратегии',
        graph: strategy,
        layout: {},
      }),
    );
    localStorage.setItem(
      'profit-fox:draft:signal:funding-high',
      JSON.stringify({
        savedAt: '2026-11-21T09:00:00Z',
        description: 'черновик сигнала',
        graph: savedFundingHigh().graph,
        layout: {},
      }),
    );
    routes({
      'GET /api/strategies/funding-high': () =>
        json({ ...savedFundingShort(1), name: 'funding-high' }),
      'GET /api/signals/funding-high': () => json(savedFundingHigh()),
    });
    const { history } = renderApp('/strategies/funding-high');

    const question = await screen.findByRole('dialog');
    await userEvent.click(within(question).getByRole('button', { name: /Восстановить/ }));
    expect(screen.getByRole('textbox', { name: 'Описание' })).toHaveValue('черновик стратегии');

    // The question about leaving the unsaved strategy is answered before the way out is taken.
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    history.push('/signals/funding-high');
    const signalQuestion = await screen.findByRole('dialog');
    await userEvent.click(within(signalQuestion).getByRole('button', { name: /Восстановить/ }));
    expect(screen.getByRole('textbox', { name: 'Описание' })).toHaveValue('черновик сигнала');
  });

  // Scenario «Сборка и сохранение»: the strategy of H0001 built on the canvas.
  it('builds the strategy of H0001 and saves it as funding-short', async () => {
    const requests = routes({
      'POST /api/strategies': async (request) =>
        json({ ...(await request.json()), version: 1, signals: [] }, 201),
      'GET /api/strategies/funding-short': () => json(savedFundingShort(2)),
    });
    renderApp('/strategies/new/abc');
    await findNode('Вход', 'enter');

    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));
    await userEvent.click(
      within(screen.getByRole('group', { name: 'Сигналы' })).getByRole('button', {
        name: 'funding-high',
      }),
    );
    await findNode('funding-high', 'funding-high');
    const enter = await selectNode('Вход', 'enter');
    await userEvent.click(
      within(within(enter).getByRole('radiogroup', { name: 'Настройка side' })).getByRole('radio', {
        name: 'short',
      }),
    );
    await userEvent.selectOptions(
      within(enter).getByRole('combobox', { name: 'Источник when' }),
      'funding-high.event',
    );
    const closeAfter = await selectNode('Закрыть через', 'close-after');
    const after = within(closeAfter).getByRole('textbox', { name: 'Значение after' });
    await userEvent.type(after, '4h');
    fireEvent.blur(after);

    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    expect(screen.getByRole('button', { name: 'Ошибки 0' })).toBeInTheDocument();
    await userEvent.click(saveButton());
    const dialog = screen.getByRole('dialog', { name: 'Имя стратегии' });
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Имя' }), 'funding-short');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Сохранить' }));

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'POST', '/api/strategies')).toHaveLength(1),
    );
    const [created] = (await bodiesOf(requests, 'POST', '/api/strategies')) as {
      name: string;
      graph: ReturnType<typeof fundingShort>;
    }[];
    expect(created!.name).toBe('funding-short');
    // Saved with the node of the position and the wires of the orders and of the position (change add-position-node).
    expect(created!.graph.nodes).toEqual([
      { id: 'enter', kind: 'enter', inputs: { size: 100 }, settings: { side: 'short' } },
      { id: 'position', kind: 'position' },
      { id: 'close-after', kind: 'close-after', inputs: { after: '4h' } },
      {
        id: 'funding-high',
        kind: 'signal',
        settings: { signal: fundingHighId, version: String(fundingHigh.latest) },
      },
    ]);
    expect(created!.graph.wires).toEqual([
      { from: 'enter.order', to: 'position.orders' },
      { from: 'position.position', to: 'close-after.position' },
      { from: 'close-after.order', to: 'position.orders' },
      { from: 'funding-high.event', to: 'enter.when' },
    ]);
  });
});

describe('a strategy of a signal', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario «Стратегия из сигнала funding-high».
  it('opens a new strategy with the node of the signal, the wire and the close-after; the signal stays', async () => {
    const requests = routes({
      'GET /api/signals/funding-high': () =>
        json({ ...savedFundingHigh(), id: fundingHighId, version: 2 }),
    });
    const { history } = renderApp('/signals/funding-high');
    await findNode('Сравнить', 'high');

    await userEvent.click(screen.getByRole('button', { name: 'Создать стратегию' }));

    await waitFor(() => expect(history.location.pathname).toMatch(/^\/strategies\/new\/.+/));
    const node = await findNode('funding-high', 'funding-high');
    expect(within(node).getByText(/^версия: 2/)).toBeInTheDocument();
    // Scenario «Стратегия из сигнала»: the draft is of ideas.
    const draftId = history.location.pathname.split('/').at(-1)!;
    expect(JSON.parse(localStorage.getItem(strategyDraftKey({ newId: draftId }))!).kind).toBe(
      'idea',
    );
    expect(screen.getByLabelText('Вид')).toHaveTextContent('категория идей');
    expect(await findNode('Вход', 'enter')).toBeInTheDocument();
    expect(await findNode('Закрыть через', 'close-after')).toBeInTheDocument();
    const enter = await selectNode('Вход', 'enter');
    expect(within(enter).getByRole('combobox', { name: 'Источник when' })).toHaveValue(
      'funding-high.event',
    );
    await userEvent.click(
      await screen.findByRole('button', { name: /Ошибки 2/ }, { timeout: 2000 }),
    );
    expect(card('Ошибки').getByText('узел enter, настройка side')).toBeInTheDocument();
    expect(card('Ошибки').getByText('close-after.after')).toBeInTheDocument();
    expect(requests.some((request) => request.method === 'PUT')).toBe(false);
    expect(
      requests.some(
        (request) =>
          request.method === 'POST' && new URL(request.url).pathname === '/api/strategies',
      ),
    ).toBe(false);
  });

  it('is offered in the row of a saved signal', async () => {
    routes({
      'GET /api/signals': () =>
        json([
          { name: 'funding-high', description: null, version: 2, error: null, id: fundingHighId },
        ]),
    });
    const { history } = renderApp('/signals');

    const row = await screen.findByRole('row', { name: 'Сигнал funding-high' });
    await userEvent.click(within(row).getByRole('button', { name: 'Создать стратегию' }));

    await waitFor(() => expect(history.location.pathname).toMatch(/^\/strategies\/new\/.+/));
    expect(await findNode('funding-high', 'funding-high')).toBeInTheDocument();
  });
});

// Spec strategy-constructor «Кнопка «Прогнать»» and «Последний прогон стратегии».
describe('the runs of a strategy', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  const version3 = () => ({ ...savedFundingShort(1), version: 3 });

  // Scenario «Прогон из редактора».
  it('opens the form of a run of the saved version from the editor', async () => {
    routes({
      'GET /api/strategies/funding-short': () => json(version3()),
      'GET /api/strategies/funding-short/versions/3': () => json(version3()),
    });
    const { history } = renderApp('/strategies/funding-short');
    await findNode('Вход', 'enter');
    await checked();

    const run = screen.getByRole('button', { name: 'Прогнать' });
    await waitFor(() => expect(run).toBeEnabled());
    await userEvent.click(run);

    await waitFor(() => expect(history.location.pathname).toBe('/strategies/funding-short/run'));
    expect(history.location.search).toBe('?version=3');
    const form = within(await screen.findByRole('form', { name: 'Запуск прогона' }));
    expect(form.getByRole('heading', { name: 'Прогон funding-short' })).toBeInTheDocument();
    expect(form.getByText('версия 3')).toBeInTheDocument();
  });

  // Scenario «Несохранённые правки».
  it('is not run with an edit that is not saved, and says why', async () => {
    routes({ 'GET /api/strategies/funding-short': () => json(version3()) });
    renderApp('/strategies/funding-short');
    const closeAfter = await selectNode('Закрыть через', 'close-after');
    await checked();
    expect(screen.getByRole('button', { name: 'Прогнать' })).toBeEnabled();

    const after = within(closeAfter).getByRole('textbox', { name: 'Значение after' });
    await userEvent.clear(after);
    await userEvent.type(after, '8h');
    fireEvent.blur(after);

    const run = screen.getByRole('button', { name: 'Прогнать' });
    await waitFor(() => expect(run).toBeDisabled());
    expect(run).toHaveAttribute('title', 'сначала сохраните стратегию');
    expect(run).toHaveAccessibleDescription('сначала сохраните стратегию');
  });

  it('is not run before the first save', async () => {
    routes();
    renderApp(newStrategy('idea'));
    await findNode('Вход', 'enter');

    const run = screen.getByRole('button', { name: 'Прогнать' });
    expect(run).toBeDisabled();
    expect(run).toHaveAttribute('title', 'сначала сохраните стратегию');
  });

  // Scenario «Прогон из списка».
  it('opens the form of a run of the latest version from the menu of a row', async () => {
    const ladder = { ...savedFundingShort(1), name: 'ladder', version: 2, kind: 'bot' };
    routes({
      'GET /api/strategies': () => json([{ ...listItem('ladder', 2), kind: 'bot' }]),
      'GET /api/strategies/ladder/versions/2': () => json(ladder),
    });
    const { history } = renderApp('/strategies');

    const row = await screen.findByRole('row', { name: 'Стратегия ladder' });
    await userEvent.click(within(row).getByRole('button', { name: 'Действия ladder' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Прогнать' }));

    await waitFor(() => expect(history.location.pathname).toBe('/strategies/ladder/run'));
    expect(history.location.search).toBe('?version=2');
    const form = within(await screen.findByRole('form', { name: 'Запуск прогона' }));
    expect(form.getByRole('heading', { name: 'Прогон ladder' })).toBeInTheDocument();
    expect(form.getByLabelText('Вид')).toHaveTextContent('для бота');
  });

  // Task 12.6, spec bench-ui «Одно состояние на трёх страницах»: the last run follows the queue the header asks for.
  it('shows the last run going once the queue says it goes, while the page is open', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let state = 'queued';
    routes({
      'GET /api/strategies': () =>
        json([{ ...listItem('funding-short', 3), lastRun: { id: 'b1', state, version: 3 } }]),
      'GET /api/backtests': (request) =>
        json(
          new URL(request.url).searchParams.get('active') === 'true'
            ? [backtest({ id: 'b1', state })]
            : [],
        ),
    });
    renderApp('/strategies');

    const row = within(await screen.findByRole('row', { name: 'Стратегия funding-short' }));
    expect(row.getByText('В очереди')).toBeInTheDocument();
    state = 'running';
    await act(() => vi.advanceTimersByTimeAsync(3100));

    await waitFor(() => expect(row.getByText('Идёт')).toHaveAttribute('data-tone', 'run'));
  });

  // Scenarios «Законченный прогон», «Прогон в очереди важнее законченного» and «Стратегия без прогонов».
  it('shows the last run in the row by its state and version, or that there was none', async () => {
    routes({
      'GET /api/strategies': () =>
        json([
          { ...listItem('funding-short', 3), lastRun: { id: 'b1', state: 'done', version: 3 } },
          { ...listItem('ladder'), kind: 'bot' },
          { ...listItem('price-drop', 2), lastRun: { id: 'b2', state: 'queued', version: 2 } },
        ]),
    });
    renderApp('/strategies');

    const funding = within(await screen.findByRole('row', { name: 'Стратегия funding-short' }));
    expect(funding.getByText('Готов')).toHaveAttribute('data-tone', 'ok');
    const link = funding.getByRole('link', { name: 'Прогон b1' });
    expect(link).toHaveAttribute('href', '/runs/b1');
    expect(within(link).getByText('v3')).toBeInTheDocument();

    const drop = within(screen.getByRole('row', { name: 'Стратегия price-drop' }));
    expect(drop.getByText('В очереди')).toHaveAttribute('data-tone', 'neutral');

    const ladder = within(screen.getByRole('row', { name: 'Стратегия ladder' }));
    expect(ladder.getByText('не запускалась')).toBeInTheDocument();
    expect(ladder.queryByRole('link', { name: /^Прогон / })).toBeNull();
  });
});
