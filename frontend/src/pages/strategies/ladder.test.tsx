import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { card, saveButton, selectNode } from '@/test/editor';
import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import ladderFile from '@/test/ladder.fixture.json';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';
import { newStrategy, strategyRoutes } from '@/test/strategies';

interface Wire {
  readonly from: string;
  readonly to: string;
}

interface Saved {
  readonly name: string;
  readonly kind: string;
  readonly graph: {
    readonly parameters: readonly { name: string; value: unknown }[];
    readonly nodes: readonly { id: string; kind: string; settings?: Record<string, string> }[];
    readonly wires: readonly Wire[];
  };
}

/** The ids the nodes of the fixture get on the canvas: a node added from the palette is named by its kind. */
const ids: Record<string, string> = {
  p: 'parameters-1',
  position: 'position',
  closed: 'not-1',
  entry: 'market-order-1',
  rise: 'add-1',
  'sell-price': 'multiply-1',
  sell: 'limit-order-1',
  drop: 'subtract-1',
  'buy-price': 'multiply-2',
  'next-invested': 'add-2',
  fits: 'compare-1',
  'may-buy': 'and-1',
  buy: 'limit-order-2',
};

function canvasWire(wire: Wire): string {
  const id = (reference: string) => {
    const [node, pin] = reference.split('.');
    return `${ids[node!] ?? node}.${pin}`;
  };
  return `${id(wire.from)} -> ${id(wire.to)}`;
}

async function addFromPalette(group: string, title: string) {
  if (screen.queryByRole('group', { name: group }) === null) {
    await userEvent.click(screen.getByRole('button', { name: 'Палитра узлов' }));
  }

  await userEvent.click(
    within(await screen.findByRole('group', { name: group })).getByRole('button', { name: title }),
  );
}

async function wire(title: string, id: string, sources: Record<string, string>) {
  const node = await selectNode(title, id);
  for (const [pin, from] of Object.entries(sources)) {
    await userEvent.selectOptions(
      within(node).getByRole('combobox', { name: `Источник ${pin}` }),
      from,
    );
  }

  return node;
}

async function value(title: string, id: string, pin: string, text: string) {
  const node = await selectNode(title, id);
  const field = within(node).getByRole('textbox', { name: `Значение ${pin}` });
  await userEvent.clear(field);
  await userEvent.type(field, text);
  fireEvent.blur(field);
}

async function setting(title: string, id: string, name: string, option: string) {
  const node = await selectNode(title, id);
  await userEvent.click(
    within(within(node).getByRole('radiogroup', { name: `Настройка ${name}` })).getByRole('radio', {
      name: option,
    }),
  );
}

describe('the ladder on the canvas', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario «Лесенка собирается на холсте» («Сборка и сохранение»), with the check of the server faked.
  it('is built from the palette and saved as ladder', { timeout: 60_000 }, async () => {
    const requests = fakeApi({
      ...baseRoutes(),
      ...strategyRoutes(),
      'POST /api/strategies': async (request) =>
        json({ ...(await request.json()), version: 1, signals: [] }, 201),
    });
    // A ladder is a strategy of a bot: it starts with the position alone.
    renderApp(newStrategy('bot'));
    await selectNode('Позиция', 'position');

    await userEvent.click(screen.getByRole('button', { name: 'Параметры' }));
    for (const name of ['priceRiseForSell', 'priceDropForBuy', 'limitPerOrder', 'limitDeposit']) {
      await userEvent.type(
        card('Параметры').getByRole('textbox', { name: 'Имя нового параметра' }),
        name,
      );
      await userEvent.click(card('Параметры').getByRole('button', { name: 'Добавить' }));
    }

    for (const [name, number] of [
      ['priceRiseForSell', '0.01'],
      ['priceDropForBuy', '0.01'],
      ['limitPerOrder', '100'],
      ['limitDeposit', '1000'],
    ] as const) {
      const field = within(
        card('Параметры').getByRole('group', { name: `Параметр ${name}` }),
      ).getByRole('textbox', { name: 'Значение параметра' });
      await userEvent.clear(field);
      await userEvent.type(field, number);
      fireEvent.blur(field);
    }

    for (const [group, title] of [
      ['Граф', 'Параметры'],
      ['Логика', 'Не'],
      ['Позиция', 'Рыночная заявка'],
      ['Числа', 'Сложить'],
      ['Числа', 'Умножить'],
      ['Позиция', 'Лимитная заявка'],
      ['Числа', 'Вычесть'],
      ['Числа', 'Умножить'],
      ['Числа', 'Сложить'],
      ['Логика', 'Сравнить'],
      ['Логика', 'И'],
      ['Позиция', 'Лимитная заявка'],
    ] as const) {
      await addFromPalette(group, title);
    }

    await wire('Не', 'not-1', { value: 'position.open' });
    await wire('Рыночная заявка', 'market-order-1', {
      when: 'not-1.result',
      size: 'parameters-1.limitPerOrder',
    });
    await value('Сложить', 'add-1', 'a', '1');
    await wire('Сложить', 'add-1', { b: 'parameters-1.priceRiseForSell' });
    await wire('Умножить', 'multiply-1', { a: 'position.averagePrice', b: 'add-1.result' });
    await setting('Лимитная заявка', 'limit-order-1', 'side', 'sell');
    await setting('Лимитная заявка', 'limit-order-1', 'sizeIn', 'quantity');
    await wire('Лимитная заявка', 'limit-order-1', {
      when: 'position.open',
      price: 'multiply-1.result',
      size: 'position.quantity',
    });
    await value('Вычесть', 'subtract-1', 'a', '1');
    await wire('Вычесть', 'subtract-1', { b: 'parameters-1.priceDropForBuy' });
    await wire('Умножить', 'multiply-2', { a: 'position.lastEntryPrice', b: 'subtract-1.result' });
    await wire('Сложить', 'add-2', { a: 'position.invested', b: 'parameters-1.limitPerOrder' });
    const fits = await wire('Сравнить', 'compare-1', {
      a: 'add-2.result',
      b: 'parameters-1.limitDeposit',
    });
    await userEvent.selectOptions(
      within(fits).getByRole('combobox', { name: 'Настройка operation' }),
      'le',
    );
    await wire('И', 'and-1', { a: 'position.open', b: 'compare-1.result' });
    await wire('Лимитная заявка', 'limit-order-2', {
      when: 'and-1.result',
      price: 'multiply-2.result',
      size: 'parameters-1.limitPerOrder',
    });
    const position = await selectNode('Позиция', 'position');
    for (const order of ['market-order-1.order', 'limit-order-1.order', 'limit-order-2.order']) {
      await userEvent.selectOptions(
        within(position).getByRole('combobox', { name: 'Добавить orders' }),
        order,
      );
    }

    expect(within(position).getAllByRole('button', { name: /^Убрать / })).toHaveLength(3);
    await waitFor(() => expect(saveButton()).toBeEnabled(), { timeout: 2000 });
    await userEvent.click(saveButton());
    const dialog = screen.getByRole('dialog', { name: 'Имя стратегии' });
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Имя' }), 'ladder');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Сохранить' }));

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'POST', '/api/strategies')).toHaveLength(1),
    );
    const [saved] = (await bodiesOf(requests, 'POST', '/api/strategies')) as Saved[];
    expect(saved!.name).toBe('ladder');
    expect(saved!.kind).toBe('bot');
    expect(saved!.graph.nodes).toHaveLength(13);
    expect(saved!.graph.parameters.map(({ name, value: number }) => [name, number])).toEqual(
      ladderFile.parameters.map(({ name, value: number }) => [name, number]),
    );
    expect(saved!.graph.wires.map((saved) => `${saved.from} -> ${saved.to}`).sort()).toEqual(
      ladderFile.wires.map(canvasWire).sort(),
    );
    expect(saved!.graph.nodes.find((node) => node.id === 'limit-order-1')!.settings).toEqual({
      side: 'sell',
      sizeIn: 'quantity',
    });
  });

  it('is told by «лимитка» in the list of strategies', async () => {
    fakeApi({
      ...baseRoutes(),
      ...strategyRoutes(),
      'GET /api/strategies': () =>
        json([
          {
            name: 'ladder',
            description: null,
            version: 1,
            error: null,
            signals: [],
            exits: ['limit-order'],
          },
        ]),
    });
    renderApp('/strategies');

    const row = await screen.findByRole('row', { name: /ladder/ });
    expect(within(row).getByText('лимитка')).toBeInTheDocument();
  });
});
