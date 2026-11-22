import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { card, checked, findNode, openFundingHigh, saveButton, selectNode } from '@/test/editor';
import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { h0001Graph } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';
import { baseRoutes, checkLikeServer } from '@/test/routes';

describe('the editor of a signal', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('draws the nodes with the pins of the catalog', async () => {
    openFundingHigh();

    const high = await findNode('Сравнить', 'high');
    expect(within(high).getByText('a')).toBeInTheDocument();
    expect(within(high).getByText('b')).toBeInTheDocument();
    expect(within(high).getByText('result')).toBeInTheDocument();
    expect(within(high).getByText('operation: ge')).toBeInTheDocument();
    expect(within(await findNode('Параметры', 'p')).getByText('threshold')).toBeInTheDocument();
  });

  // Scenario «Ошибка у пина».
  it('marks the pin of an error, lists it and keeps «Сохранить» off', async () => {
    openFundingHigh();

    const node = await selectNode('Сравнить', 'high');
    await userEvent.selectOptions(within(node).getByRole('combobox', { name: 'Источник b' }), '');

    const high = await findNode('Сравнить', 'high');
    await waitFor(() => expect(high).toHaveAttribute('aria-invalid', 'true'), { timeout: 2000 });
    expect(within(high).getByText('b').closest('[data-bad="true"]')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Ошибки 1' })).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Ошибки 1' }));
    const errors = card('Ошибки');
    expect(errors.getByText('high.b')).toBeInTheDocument();
    await userEvent.click(errors.getByRole('button', { name: 'Показать на холсте' }));
    expect(screen.getByRole('region', { name: 'Узел: Сравнить high' })).toBeInTheDocument();
  });

  // Scenario «Ответ на старую правку».
  it('shows the errors of the last edit when an older check comes back later', async () => {
    // The check of the opened graph answers at once; the checks of the two edits wait until the test answers them.
    const answers: { request: Request; answer: (response: Response) => void }[] = [];
    let checks = 0;
    openFundingHigh({
      'POST /api/signals/validate': (request) =>
        ++checks === 1
          ? Promise.resolve(json({ errors: [] }))
          : new Promise((resolve) => answers.push({ request, answer: resolve })),
    });
    await checked();

    const node = await selectNode('Сравнить', 'high');
    await userEvent.selectOptions(within(node).getByRole('combobox', { name: 'Источник b' }), '');
    await waitFor(() => expect(answers).toHaveLength(1), { timeout: 2000 });
    const value = within(node).getByRole('textbox', { name: 'Значение b' });
    await userEvent.type(value, '0.001');
    fireEvent.blur(value);
    await waitFor(() => expect(answers).toHaveLength(2), { timeout: 2000 });

    answers[1]!.answer(await checkLikeServer(answers[1]!.request));
    answers[0]!.answer(await checkLikeServer(answers[0]!.request));

    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    expect(screen.getByRole('button', { name: 'Ошибки 0' })).toBeInTheDocument();
    expect(await findNode('Сравнить', 'high')).toHaveAttribute('aria-invalid', 'false');
  });

  // Scenario «Новый параметр».
  it('adds a parameter that becomes an output of the parameters node', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');

    await userEvent.click(screen.getByRole('button', { name: 'Параметры' }));
    await userEvent.type(
      card('Параметры').getByRole('textbox', { name: 'Имя нового параметра' }),
      'level',
    );
    await userEvent.click(card('Параметры').getByRole('button', { name: 'Добавить' }));

    expect(within(await findNode('Параметры', 'p')).getByText('level')).toBeInTheDocument();
  });

  // Change add-ideas-and-market, task 3.5: a signal may declare a boolean output — a state.
  it('declares the output of the signal a boolean, and drops the wire of the event into it', async () => {
    openFundingHigh();
    await findNode('Выходы', 'out');

    await userEvent.click(screen.getByRole('button', { name: 'Параметры' }));
    const type = card('Параметры').getByRole('combobox', { name: 'Тип выхода сигнала' });
    expect(type).toHaveValue('event');
    await userEvent.selectOptions(type, 'boolean');

    await waitFor(() => expect(type).toHaveValue('boolean'));
    await userEvent.click(screen.getByRole('button', { name: 'JSON' }));
    const text = (card('JSON').getByRole('textbox') as HTMLTextAreaElement).value;
    const graph = JSON.parse(text) as {
      outputs: { name: string; type: string }[];
      wires: { from: string; to: string }[];
    };
    expect(graph.outputs).toEqual([{ name: 'entry', type: 'boolean' }]);
    expect(graph.wires.some((wire) => wire.to === 'out.entry')).toBe(false);
  });

  // Change add-ideas-and-market, task 4.4: the pair of a node «Свеча пары» is chosen from the hint of the traded pairs.
  it('chooses the pair of a candle of another pair from the hint, and the pair goes into the graph', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');

    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));
    await userEvent.click(
      within(screen.getByRole('region', { name: 'Палитра' })).getByRole('button', {
        name: 'Свеча пары',
      }),
    );
    const node = await selectNode('Свеча пары', 'pair-candle-1');
    const symbol = within(node).getByRole('combobox', { name: 'Настройка symbol' });
    const hint = document.getElementById(symbol.getAttribute('list')!)!;
    await waitFor(() =>
      expect([...hint.querySelectorAll('option')].map((option) => option.value)).toEqual([
        'BTCUSDT',
        'PRLUSDT',
      ]),
    );

    await userEvent.type(symbol, 'btcusdt{Enter}');

    expect(symbol).toHaveValue('BTCUSDT');
    await userEvent.click(screen.getByRole('button', { name: 'JSON' }));
    const graph = JSON.parse((card('JSON').getByRole('textbox') as HTMLTextAreaElement).value) as {
      nodes: { id: string; settings?: Record<string, string> }[];
    };
    expect(graph.nodes.find((item) => item.id === 'pair-candle-1')!.settings).toEqual({
      symbol: 'BTCUSDT',
    });
  });

  // Scenario «Вставка графа».
  it('replaces the graph with pasted JSON, and keeps it when the JSON does not read', async () => {
    openFundingHigh();
    await findNode('Впервые', 'first');
    await userEvent.click(screen.getByRole('button', { name: 'JSON' }));
    const text = card('JSON').getByRole('textbox', { name: 'Граф JSON' });

    fireEvent.change(text, { target: { value: '{ not json' } });
    await userEvent.click(card('JSON').getByRole('button', { name: 'Заменить граф' }));
    expect(card('JSON').getByText(/Это не JSON/)).toBeInTheDocument();
    expect(await findNode('Впервые', 'first')).toBeInTheDocument();

    const pasted = h0001Graph();
    const smaller = {
      ...pasted,
      nodes: pasted.nodes.filter((node) => node.id !== 'first'),
      wires: pasted.wires.filter(
        (wire) => !wire.from.startsWith('first') && !wire.to.startsWith('first'),
      ),
    };
    fireEvent.change(text, { target: { value: JSON.stringify(smaller) } });
    await userEvent.click(card('JSON').getByRole('button', { name: 'Заменить граф' }));

    await waitFor(() => expect(screen.queryByRole('group', { name: 'Впервые first' })).toBeNull());
    expect(screen.getByRole('group', { name: 'Сравнить high' })).toBeInTheDocument();
  });
});

describe('the draft of a signal', () => {
  afterEach(() => vi.unstubAllGlobals());

  async function changeThreshold(to: string) {
    await findNode('Параметры', 'p');
    await userEvent.click(screen.getByRole('button', { name: 'Параметры' }));
    const value = card('Параметры').getByRole('textbox', { name: 'Значение параметра' });
    await userEvent.clear(value);
    await userEvent.type(value, to);
    fireEvent.blur(value);
  }

  // Scenario «Закрытая вкладка».
  it('is offered back after the tab was closed without saving', async () => {
    const first = openFundingHigh();
    await changeThreshold('0.001');
    await waitFor(() =>
      expect(localStorage.getItem('profit-fox:draft:signal:funding-high')).not.toBeNull(),
    );
    first.unmount();

    openFundingHigh();
    const question = await screen.findByRole('dialog', { name: 'Черновик' });
    expect(within(question).getByText(/узлов 0, проводов 0/)).toBeInTheDocument();
    await userEvent.click(within(question).getByRole('button', { name: 'Восстановить' }));

    await userEvent.click(screen.getByRole('button', { name: 'Параметры' }));
    expect(card('Параметры').getByRole('textbox', { name: 'Значение параметра' })).toHaveValue(
      '0.001',
    );
  });

  // Scenario «Сохранение убирает черновик».
  it('goes once the edit is saved', async () => {
    const first = openFundingHigh();
    await changeThreshold('0.001');
    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    await userEvent.click(saveButton());
    await waitFor(async () =>
      expect(await bodiesOf(first.requests, 'PUT', '/api/signals/funding-high')).toHaveLength(1),
    );
    const [put] = (await bodiesOf(first.requests, 'PUT', '/api/signals/funding-high')) as {
      graph: { parameters: { value: number }[] };
      layout: object;
    }[];
    expect(put!.graph.parameters[0]!.value).toBe(0.001);
    await waitFor(() =>
      expect(screen.getByRole('status', { name: 'Состояние' })).toHaveTextContent('сохранено'),
    );
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(localStorage.getItem('profit-fox:draft:signal:funding-high')).toBeNull();
    first.unmount();

    openFundingHigh();
    await findNode('Параметры', 'p');
    expect(screen.queryByRole('dialog', { name: 'Черновик' })).toBeNull();
  });
});

describe('a new signal', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Новый сигнал».
  it('starts with the outputs node only and cannot be saved before entry has a wire', async () => {
    fakeApi(baseRoutes());
    renderApp('/signals/new/abc');

    expect(await findNode('Выходы', 'out')).toBeInTheDocument();
    expect(
      screen.getAllByRole('group').filter((group) => group.classList.contains('relative')),
    ).toHaveLength(1);
    await waitFor(
      () => expect(screen.getByRole('button', { name: 'Ошибки 1' })).toBeInTheDocument(),
      { timeout: 2000 },
    );
    expect(saveButton()).toBeDisabled();
  });

  // Scenario «Занятое имя» at the first save.
  it('shows a taken name in the dialog of the first save', async () => {
    localStorage.setItem(
      'profit-fox:draft:signal-new:abc',
      JSON.stringify({
        savedAt: '2026-11-19T00:00:00Z',
        description: '',
        graph: h0001Graph(),
        layout: {},
      }),
    );
    const requests = fakeApi({
      ...baseRoutes(),
      'POST /api/signals': () =>
        json(
          {
            errors: [
              { code: 'NameTaken', reason: 'the name funding-high is taken', field: 'name' },
            ],
          },
          409,
        ),
    });
    renderApp('/signals/new/abc');
    await findNode('Сравнить', 'high');

    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    await userEvent.click(saveButton());
    const dialog = screen.getByRole('dialog', { name: 'Имя сигнала' });
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Имя' }), 'funding-high');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Сохранить' }));

    expect(await within(dialog).findByText('the name funding-high is taken')).toBeInTheDocument();
    const [created] = (await bodiesOf(requests, 'POST', '/api/signals')) as {
      name: string;
      graph: unknown;
    }[];
    expect(created!.name).toBe('funding-high');
    expect(created!.graph).toEqual(h0001Graph());
  });
});

describe('the version of a signal', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Версии сигнала»: the next version after a save with a new threshold.
  it('shows the next version after a save with a new threshold', async () => {
    openFundingHigh();
    expect(await screen.findByText('версия 1')).toBeInTheDocument();

    await findNode('Параметры', 'p');
    await userEvent.click(screen.getByRole('button', { name: 'Параметры' }));
    const value = card('Параметры').getByRole('textbox', { name: 'Значение параметра' });
    await userEvent.clear(value);
    await userEvent.type(value, '0.001');
    fireEvent.blur(value);
    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    await userEvent.click(saveButton());

    expect(await screen.findByText('версия 2')).toBeInTheDocument();
    expect(screen.queryByText('версия 1')).toBeNull();
  });
});
