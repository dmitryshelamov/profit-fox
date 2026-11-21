import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { card, checked, findNode, saveButton, selectNode } from '@/test/editor';
import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes, savedFundingHigh } from '@/test/routes';
import {
  fundingHigh,
  fundingHighId,
  fundingShort,
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

    const first = await screen.findByRole('listitem', { name: 'Стратегия funding-short' });
    expect(within(first).getByText('версия 2')).toBeInTheDocument();
    const second = screen.getByRole('listitem', { name: 'Стратегия price-drop-long' });
    expect(within(second).getByText('версия 1')).toBeInTheDocument();
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

    const row = await screen.findByRole('listitem', { name: 'Стратегия price-drop-long' });
    await userEvent.click(within(row).getByRole('button', { name: 'Переименовать' }));
    const field = within(row).getByRole('textbox', { name: 'Новое имя' });
    await userEvent.clear(field);
    await userEvent.type(field, 'funding-short');
    await userEvent.click(within(row).getAllByRole('button', { name: 'Переименовать' }).at(-1)!);

    expect(await within(row).findByText('the name funding-short is taken')).toBeInTheDocument();
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
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderApp('/strategies');

    const row = await screen.findByRole('listitem', { name: 'Стратегия funding-short' });
    await userEvent.click(within(row).getByRole('button', { name: 'Удалить' }));

    expect(await screen.findByText(/Стратегий нет/)).toBeInTheDocument();
    expect(requests.filter((request) => request.method === 'DELETE')).toHaveLength(1);
  });

  // Scenario «Новая стратегия».
  it('opens a new strategy of an enter and a close-after that is not in the list', async () => {
    const requests = routes();
    renderApp('/strategies');

    await userEvent.click(await screen.findByRole('button', { name: 'Новая стратегия' }));

    expect(await findNode('Вход', 'enter')).toBeInTheDocument();
    expect(await findNode('Закрыть через', 'close-after')).toBeInTheDocument();
    expect(
      requests.some(
        (request) =>
          request.method === 'POST' && new URL(request.url).pathname === '/api/strategies',
      ),
    ).toBe(false);
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

    history.push('/signals/funding-high');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
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
    await userEvent.selectOptions(
      within(enter).getByRole('combobox', { name: 'Настройка side' }),
      'short',
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
    expect(created!.graph.nodes).toEqual([
      { id: 'enter', kind: 'enter', settings: { side: 'short' } },
      { id: 'close-after', kind: 'close-after', inputs: { after: '4h' } },
      {
        id: 'funding-high',
        kind: 'signal',
        settings: { signal: fundingHighId, version: String(fundingHigh.latest) },
      },
    ]);
    expect(created!.graph.wires).toEqual([{ from: 'funding-high.event', to: 'enter.when' }]);
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

    const row = await screen.findByRole('listitem', { name: 'Сигнал funding-high' });
    await userEvent.click(within(row).getByRole('button', { name: 'Создать стратегию' }));

    await waitFor(() => expect(history.location.pathname).toMatch(/^\/strategies\/new\/.+/));
    expect(await findNode('funding-high', 'funding-high')).toBeInTheDocument();
  });
});
