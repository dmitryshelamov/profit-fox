import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { toFlow } from '@/modules/graph-editor/model/flow';
import { coloredWires } from '@/modules/graph-editor/ui/GraphCanvas';
import { pinLooks } from '@/modules/graph-editor/ui/looks';
import { checked, findNode, openFundingHigh, selectNode } from '@/test/editor';
import { fakeApi, json } from '@/test/fakeApi';
import { catalog, h0001Graph } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';
import { baseRoutes, savedFundingHigh } from '@/test/routes';
import { fundingHighId, fundingShort, savedFundingShort, strategyRoutes } from '@/test/strategies';

/** The handle of a pin of a node on the canvas. */
function pinOf(node: HTMLElement, pin: string): HTMLElement {
  const handle = node.querySelector<HTMLElement>(`[data-handleid="${pin}"]`);
  if (handle === null) {
    throw new Error(`The node has no pin ${pin}.`);
  }

  return handle;
}

const groupOfNode = (node: HTMLElement) =>
  node.querySelector('[data-group]')?.getAttribute('data-group');

describe('the bars of the editor', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Change redesign-bench-ui, task 5.1: one main action on the screen of the editor.
  it('has «Сохранить» as the only button of the main look over the canvas', async () => {
    openFundingHigh({
      'GET /api/signals/funding-high': () => json({ ...savedFundingHigh(), id: fundingHighId }),
    });
    await findNode('Сравнить', 'high');
    await checked();

    const preview = screen.getByRole('region', { name: 'Предпросмотр' });
    const primary = [...document.querySelectorAll('[data-variant="primary"]')].filter(
      (button) => !preview.contains(button),
    );
    expect(primary.map((button) => button.textContent)).toEqual(['Сохранить']);
    expect(
      within(screen.getByRole('toolbar', { name: 'Действия' }))
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Ошибки 0', 'Параметры', 'JSON', 'Отбросить', 'Сохранить', 'Создать стратегию']);
  });

  it('tells the state of the document under the name «Состояние»', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');
    await checked();
    const state = screen.getByRole('status', { name: 'Состояние' });
    expect(state).toHaveTextContent('сохранено');
    expect(state).toHaveAttribute('data-state', 'saved');

    await userEvent.type(screen.getByRole('textbox', { name: 'Описание' }), 'порог фандинга');

    expect(state).toHaveTextContent('не сохранено');
    expect(state).toHaveAttribute('data-state', 'dirty');
  });

  it('has the tools on the left: the palette, the search of a node, the fit and «Выстроить»', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');

    const tools = within(screen.getByRole('toolbar', { name: 'Инструменты' }));
    expect(tools.getAllByRole('button').map((button) => button.getAttribute('aria-label'))).toEqual(
      ['Палитра узлов', 'Найти узел на холсте', 'Вписать в экран', 'Выстроить'],
    );
    expect(screen.queryByRole('button', { name: 'Разложить' })).toBeNull();
    const scale = within(screen.getByRole('toolbar', { name: 'Масштаб' }));
    expect(scale.getAllByRole('button').map((button) => button.getAttribute('aria-label'))).toEqual(
      ['Уменьшить', 'Увеличить', 'Вписать'],
    );
    expect(scale.getByText(/^\d+%$/)).toBeInTheDocument();
  });

  it('keeps only the search and the fit for a hypothesis, which is read only', async () => {
    fakeApi(baseRoutes());
    renderApp('/hypotheses/H0001');
    await findNode('Сравнить', 'high');

    const tools = within(screen.getByRole('toolbar', { name: 'Инструменты' }));
    expect(tools.getAllByRole('button').map((button) => button.getAttribute('aria-label'))).toEqual(
      ['Найти узел на холсте', 'Вписать в экран'],
    );
    const primary = [...document.querySelectorAll('[data-variant="primary"]')].filter(
      (button) => !screen.getByRole('region', { name: 'Предпросмотр' }).contains(button),
    );
    expect(primary.map((button) => button.textContent)).toEqual(['Скопировать в сигнал']);
  });
});

// Spec signal-constructor «Цвет группы узла и типа пина».
describe('the colors of the canvas', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Провод по типу»: the pins of the two wires on the canvas.
  it('gives the pins of a wire the type of its value', async () => {
    openFundingHigh();
    const high = await findNode('Сравнить', 'high');
    const first = await findNode('Впервые', 'first');
    const out = await findNode('Выходы', 'out');

    for (const pin of [pinOf(high, 'result'), pinOf(first, 'value')]) {
      expect(pin).toHaveAttribute('data-type', 'boolean');
      expect(pin).toHaveAttribute('data-connected', 'true');
    }

    for (const pin of [pinOf(first, 'event'), pinOf(out, 'entry')]) {
      expect(pin).toHaveAttribute('data-type', 'event');
      expect(pin).toHaveAttribute('data-connected', 'true');
    }
  });

  // Scenario «Провод по типу»: the wires themselves, as the canvas is given them.
  it('colors a wire by the type of the output it comes from', () => {
    const document = { graph: h0001Graph(), layout: {} };
    const wires = coloredWires(toFlow(document, catalog).edges, document, catalog, []);
    const wire = (id: string) => wires.find((edge) => edge.id === id)!;

    expect(wire('high.result->first.value').data).toEqual({ type: 'boolean', bad: false });
    expect(wire('high.result->first.value').className).toBe('wire-boolean');
    expect(wire('high.result->first.value').style?.stroke).toBe(pinLooks.boolean.stroke);
    expect(wire('first.event->out.entry').data).toEqual({ type: 'event', bad: false });
    expect(wire('first.event->out.entry').style?.stroke).toBe(pinLooks.event.stroke);
    expect(wire('rate.value->high.a').style?.stroke).toBe(pinLooks.number.stroke);
    expect(new Set(Object.values(pinLooks).map((look) => look.stroke)).size).toBe(4);
  });

  it('colors a wire with an error as an error, whatever its type', () => {
    const document = { graph: h0001Graph(), layout: {} };
    const wires = coloredWires(toFlow(document, catalog).edges, document, catalog, [
      {
        wire: { from: 'high.result', to: 'first.value' },
        node: 'first',
        pin: 'value',
        place: 'провод high.result → first.value',
        code: 'TypeMismatch',
        reason: 'types differ',
      },
    ]);

    const bad = wires.find((edge) => edge.id === 'high.result->first.value')!;
    expect(bad.data).toEqual({ type: 'boolean', bad: true });
    expect(bad.style?.stroke).toBe('var(--color-err)');
  });

  // Scenario «Пин без провода».
  it('draws a pin without a wire as an outline of the color of its type', async () => {
    openFundingHigh();
    const node = await selectNode('Сравнить', 'high');
    const high = await findNode('Сравнить', 'high');
    expect(pinOf(high, 'b')).toHaveAttribute('data-connected', 'true');

    await userEvent.selectOptions(within(node).getByRole('combobox', { name: 'Источник b' }), '');

    const b = pinOf(await findNode('Сравнить', 'high'), 'b');
    expect(b).toHaveAttribute('data-type', 'number');
    expect(b).toHaveAttribute('data-connected', 'false');
    expect(b.className).toContain('border-pin-number');
    expect(b.className).not.toContain('bg-pin-number');
    expect(pinOf(await findNode('Сравнить', 'high'), 'a').className).toContain('bg-pin-number');
  });

  it('names the four types in the legend', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');

    const legend = within(screen.getByRole('list', { name: 'Типы значений' }));
    expect(
      legend.getAllByRole('listitem').map((item) => [item.dataset.type, item.textContent]),
    ).toEqual([
      ['number', 'число'],
      ['boolean', 'логическое'],
      ['duration', 'длительность'],
      ['event', 'событие'],
    ]);
  });

  it('gives the header of a node the color of its group', async () => {
    openFundingHigh();

    expect(groupOfNode(await findNode('Параметры', 'p'))).toBe('graph');
    expect(groupOfNode(await findNode('Фандинг', 'rate'))).toBe('data');
    expect(groupOfNode(await findNode('Сравнить', 'high'))).toBe('logic');
    expect(groupOfNode(await findNode('Выходы', 'out'))).toBe('graph');
  });

  it('gives a signal node the color of the signals and the badge of its version', async () => {
    fakeApi({
      ...baseRoutes(),
      ...strategyRoutes(),
      'GET /api/strategies/funding-short': () => json(savedFundingShort(2)),
    });
    renderApp('/strategies/funding-short');

    const signal = await findNode('funding-high', 's');
    expect(groupOfNode(signal)).toBe('signal');
    expect(within(signal).getByText('сигнал v2')).toBeInTheDocument();
    expect(groupOfNode(await findNode('Вход', 'enter'))).toBe('position');
  });

  it('keeps the mark of an error on a node', async () => {
    openFundingHigh();
    const node = await selectNode('Сравнить', 'high');
    await userEvent.selectOptions(within(node).getByRole('combobox', { name: 'Источник b' }), '');

    const high = await findNode('Сравнить', 'high');
    await waitFor(() => expect(high).toHaveAttribute('aria-invalid', 'true'), { timeout: 2000 });
    expect(high.className).toContain('border-err');
    expect(await findNode('Впервые', 'first')).toHaveAttribute('aria-invalid', 'false');
  });
});

// Spec signal-constructor «Поиск узла на холсте».
describe('the search of a node on the canvas', () => {
  afterEach(() => vi.unstubAllGlobals());

  async function search(text: string) {
    if (screen.queryByRole('search', { name: 'Поиск узла' }) === null) {
      await userEvent.click(screen.getByRole('button', { name: 'Найти узел на холсте' }));
    }

    const field = screen.getByRole('searchbox', { name: 'Название или id узла' });
    await userEvent.clear(field);
    await userEvent.type(field, `${text}{Enter}`);
  }

  // Scenario «По идентификатору».
  it('selects the first node whose id holds the text and opens its card', async () => {
    openFundingHigh();
    await findNode('Впервые', 'first');

    await search('fir');

    expect(screen.getByRole('region', { name: 'Узел: Впервые first' })).toBeInTheDocument();
    expect(screen.queryByText('Узел не найден')).toBeNull();
  });

  it('finds a node by a part of its title, whatever the case', async () => {
    openFundingHigh();
    await findNode('Сравнить', 'high');

    await search('СРАВ');

    expect(screen.getByRole('region', { name: 'Узел: Сравнить high' })).toBeInTheDocument();
  });

  // Scenario «Узла нет».
  it('says that there is no such node and keeps the selection', async () => {
    openFundingHigh();
    await selectNode('Сравнить', 'high');

    await search('zzz');

    expect(screen.getByRole('status', { name: '' })).toHaveTextContent('Узел не найден');
    expect(screen.getByRole('region', { name: 'Узел: Сравнить high' })).toBeInTheDocument();
  });
});

// Spec signal-constructor «Поиск в палитре узлов».
describe('the search in the palette', () => {
  afterEach(() => vi.unstubAllGlobals());

  async function palette() {
    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));
    return screen.getByRole('region', { name: 'Палитра' });
  }

  const groupsOf = (region: HTMLElement) =>
    within(region)
      .queryAllByRole('group')
      .map((group) => group.getAttribute('aria-label'));

  // Scenario «Часть названия».
  it('keeps the kinds whose title holds the text, by groups, and hides the other groups', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');
    const region = await palette();
    const field = within(region).getByRole('searchbox', { name: 'Найти узел' });
    expect(field).toHaveFocus();
    expect(groupsOf(region)).toEqual(['Граф', 'Данные', 'Логика']);

    await userEvent.type(field, 'срав');

    expect(groupsOf(region)).toEqual(['Логика']);
    expect(
      within(region)
        .getAllByRole('button')
        .map((button) => button.getAttribute('aria-label') ?? button.textContent),
    ).toEqual([expect.stringMatching(/^Сравнить/)]);
    expect(within(region).getByRole('button', { name: 'Сравнить' })).toBeInTheDocument();

    await userEvent.clear(field);
    expect(groupsOf(region)).toEqual(['Граф', 'Данные', 'Логика']);
  });

  it('finds a kind by its kind as the graph writes it', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');
    const region = await palette();

    await userEvent.type(within(region).getByRole('searchbox', { name: 'Найти узел' }), 'FIRST-t');

    expect(groupsOf(region)).toEqual(['Логика']);
    expect(within(region).getByRole('button', { name: 'Впервые' })).toBeInTheDocument();
  });

  // Scenario «Ничего не подошло».
  it('says that no node is found', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');
    const region = await palette();

    await userEvent.type(within(region).getByRole('searchbox', { name: 'Найти узел' }), 'zzz');

    expect(groupsOf(region)).toEqual([]);
    expect(within(region).queryAllByRole('button')).toHaveLength(0);
    expect(within(region).getByText('Узлов не найдено')).toBeInTheDocument();
  });

  it('adds the clicked kind as a node and closes', async () => {
    openFundingHigh();
    await findNode('Параметры', 'p');
    const region = await palette();

    await userEvent.click(within(region).getByRole('button', { name: 'Сравнить' }));

    await waitFor(() =>
      expect(screen.getAllByRole('group', { name: /^Сравнить / })).toHaveLength(2),
    );
    expect(screen.queryByRole('region', { name: 'Палитра' })).toBeNull();
  });

  it('searches the saved signals too in the editor of a strategy', async () => {
    fakeApi({
      ...baseRoutes(),
      ...strategyRoutes(),
      'GET /api/signals': () =>
        json([
          { name: 'funding-high', description: null, version: 2, error: null, id: fundingHighId },
          { name: 'price-drop', description: null, version: 1, error: null, id: 'b1' },
        ]),
    });
    renderApp('/strategies/new/abc');
    await findNode('Вход', 'enter');
    const region = await palette();

    await userEvent.type(within(region).getByRole('searchbox', { name: 'Найти узел' }), 'PRICE');

    expect(groupsOf(region)).toEqual(['Сигналы']);
    expect(
      within(within(region).getByRole('group', { name: 'Сигналы' }))
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['price-drop']);
  });
});

// Change redesign-bench-ui, task 5.4: the cards in the shared parts.
describe('the cards of the editor', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('offers a setting of two values as segments and a longer one as a list', async () => {
    const graph = fundingShort();
    fakeApi({
      ...baseRoutes(),
      ...strategyRoutes(),
      'GET /api/strategies/funding-short': () => json(savedFundingShort(2, graph)),
    });
    renderApp('/strategies/funding-short');

    const enter = await selectNode('Вход', 'enter');
    const side = within(enter).getByRole('radiogroup', { name: 'Настройка side' });
    expect(
      within(side)
        .getAllByRole('radio')
        .map((radio) => [radio.textContent, radio.getAttribute('aria-checked')]),
    ).toEqual([
      ['long', 'false'],
      ['short', 'true'],
    ]);
    await userEvent.click(within(side).getByRole('radio', { name: 'long' }));
    expect(within(await findNode('Вход', 'enter')).getByText('side: long')).toBeInTheDocument();
  });

  it('keeps a setting of more values as a list', async () => {
    openFundingHigh();
    const high = await selectNode('Сравнить', 'high');

    const operation = within(high).getByRole('combobox', { name: 'Настройка operation' });
    expect(operation).toHaveValue('ge');
    expect(within(high).queryByRole('radiogroup')).toBeNull();
  });

  it('names the node of an error in the card of errors and selects it by «Показать на холсте»', async () => {
    openFundingHigh();
    const node = await selectNode('Сравнить', 'high');
    await userEvent.selectOptions(within(node).getByRole('combobox', { name: 'Источник b' }), '');
    await userEvent.click(
      await screen.findByRole('button', { name: 'Ошибки 1' }, { timeout: 2000 }),
    );

    const errors = within(screen.getByRole('region', { name: 'Ошибки' }));
    expect(errors.getByRole('heading', { name: 'Ошибки графа' })).toBeInTheDocument();
    expect(errors.getByText('Сравнить')).toBeInTheDocument();
    expect(errors.getByText('high.b')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Узел: Сравнить high' })).toBeNull();

    await userEvent.click(errors.getByRole('button', { name: 'Показать на холсте' }));

    expect(screen.getByRole('region', { name: 'Узел: Сравнить high' })).toBeInTheDocument();
  });
});
