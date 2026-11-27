import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bodiesOf, fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { idea, ideaPage, scannerState } from '@/test/ideas';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

const signal = (name: string, label?: { enabled: boolean; text: string; timeframe: string }) => ({
  name,
  description: null,
  version: 1,
  error: null,
  id: `${name}-id`,
  strategies: [],
  output: 'event',
  ...(label ? { label } : {}),
});

function routes(signals: unknown[], extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/signals': () => json(signals),
    'GET /api/strategies': () => json([]),
    'GET /api/ideas/state': () => json(scannerState({ burst: { '15m': 12, '1h': 8, '4h': 6 } })),
    ...extra,
  });
}

// Spec idea-labels «Метка в списке сигналов» (change add-ideas-and-market, task 15.3).
describe('the label of a signal in the list', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenarios «Сигнал с меткой» and «Сигнал без метки».
  it('shows the switch on with the badge and timeframe, or off with «нет»', async () => {
    routes([
      signal('btc-drop-4h', { enabled: true, text: '↓ BTC', timeframe: '4h' }),
      signal('funding-spike'),
    ]);
    renderApp('/signals');

    const drop = within(await screen.findByRole('row', { name: 'Сигнал btc-drop-4h' }));
    expect(drop.getByRole('switch', { name: 'Метка btc-drop-4h' })).toBeChecked();
    expect(drop.getByRole('button', { name: 'Метка на идеях btc-drop-4h' })).toHaveTextContent(
      '↓ BTC4h',
    );
    const spike = within(screen.getByRole('row', { name: 'Сигнал funding-spike' }));
    expect(spike.getByRole('switch', { name: 'Метка funding-spike' })).not.toBeChecked();
    expect(spike.getByRole('button', { name: 'Метка на идеях funding-spike' })).toHaveTextContent(
      'нет',
    );
    expect(screen.getAllByRole('columnheader').map((cell) => cell.textContent)).toContain(
      'Метка на идеях',
    );
  });

  // Spec idea-labels «Включение метки».
  it('turns a label on in the window with its text and timeframe', async () => {
    const requests = routes([signal('btc-drop-4h')], {
      'PUT /api/signals/btc-drop-4h/label': async (request) => json(await request.json()),
    });
    renderApp('/signals');
    const row = within(await screen.findByRole('row', { name: 'Сигнал btc-drop-4h' }));

    await userEvent.click(row.getByRole('switch', { name: 'Метка btc-drop-4h' }));
    const window = within(screen.getByRole('dialog', { name: 'Метка btc-drop-4h' }));
    await userEvent.type(window.getByLabelText('Текст метки'), '↓ BTC');
    await userEvent.selectOptions(window.getByLabelText('Таймфрейм метки'), '4h');
    await userEvent.click(window.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await bodiesOf(requests, 'PUT', '/api/signals/btc-drop-4h/label')).toEqual([
      { enabled: true, text: '↓ BTC', timeframe: '4h' },
    ]);
  });

  it('keeps the window open with the reason when the server refuses', async () => {
    routes([signal('funding-spike')], {
      'PUT /api/signals/funding-spike/label': () =>
        json(
          { errors: [{ code: 'TakenText', reason: 'такая метка уже есть: ↓ BTC', field: 'text' }] },
          422,
        ),
    });
    renderApp('/signals');
    await userEvent.click(
      await screen.findByRole('button', { name: 'Метка на идеях funding-spike' }),
    );
    const window = within(screen.getByRole('dialog', { name: 'Метка funding-spike' }));
    await userEvent.type(window.getByLabelText('Текст метки'), '↓ BTC');

    await userEvent.click(window.getByRole('button', { name: 'Сохранить' }));

    expect(await window.findByRole('alert', { name: 'Отказ' })).toHaveTextContent(
      'такая метка уже есть: ↓ BTC',
    );
    expect(screen.getByRole('dialog', { name: 'Метка funding-spike' })).toBeInTheDocument();
  });

  // Scenario «Строка всплеска».
  it('ends with the built-in burst and its thresholds, without a switch or an editor', async () => {
    routes([signal('btc-drop-4h'), signal('funding-spike')]);
    renderApp('/signals');
    await screen.findByRole('row', { name: 'Сигнал btc-drop-4h' });

    const rows = screen.getAllByRole('row');
    expect(rows.at(-1)).toHaveAccessibleName('Встроенная метка всплеск');
    const burst = within(rows.at(-1)!);
    expect(burst.getByText('⚡ всплеск')).toBeInTheDocument();
    await waitFor(() =>
      expect(burst.getByText('15m от 12 · 1h от 8 · 4h от 6')).toBeInTheDocument(),
    );
    expect(burst.queryByRole('switch')).toBeNull();
    expect(burst.queryByRole('link')).toBeNull();
  });
});

// Spec idea-labels «Метки в списках идей» (task 15.3).
describe('the labels in the lists of ideas', () => {
  afterEach(() => vi.unstubAllGlobals());

  const ideasRoutes = (labelled: ReturnType<typeof idea>[]) =>
    routes([signal('btc-drop-4h', { enabled: true, text: '↓ BTC', timeframe: '4h' })], {
      'GET /api/strategies': () =>
        json([
          {
            name: 'breakout-long',
            description: null,
            version: 7,
            error: null,
            signals: [],
            exits: [],
            kind: 'idea',
            ideas: { enabled: true, timeframes: ['1h'] },
          },
        ]),
      'GET /api/ideas': ideaPage(labelled),
      'GET /api/ideas/summary': () =>
        json({ items: [], total: 0, page: 1, pageSize: 50, filterGone: false }),
      'GET /api/filters': () => json([]),
      'GET /api/filters/fields': () => json([]),
    });

  // Scenarios «Три метки» and «Идея без меток»: in the tables of «Идеи» one badge and «+2» (task 21.3); the table of the
  // ideas of a pair keeps two badges (pair.test.tsx).
  it('shows one badge and «+2» with the whole list in the hint, and a dash without labels', async () => {
    ideasRoutes([
      idea({ id: 'a', symbol: 'XRPUSDT', labels: ['↑ funding', '⚡ всплеск', '↓ BTC'] }),
      idea({ id: 'b', symbol: 'SOLUSDT', labels: [] }),
    ]);
    renderApp('/ideas');

    const xrp = await screen.findByRole('row', { name: 'XRPUSDT breakout-long 1h' });
    const labels = within(xrp).getAllByRole('cell')[4]!;
    expect(within(labels).getByText('↑ funding')).toBeInTheDocument();
    expect(within(labels).queryByText('⚡ всплеск')).toBeNull();
    expect(within(labels).queryByText('↓ BTC')).toBeNull();
    expect(within(labels).getByText('+2')).toBeInTheDocument();
    expect(labels.firstElementChild).toHaveAttribute('title', '↑ funding, ⚡ всплеск, ↓ BTC');
    const sol = screen.getByRole('row', { name: 'SOLUSDT breakout-long 1h' });
    expect(within(sol).getAllByRole('cell')[4]).toHaveTextContent('—');
  });

  // Scenario «Отбор по метке».
  it('offers the labels turned on and the burst, and asks the ideas with the label chosen', async () => {
    const requests = ideasRoutes([idea()]);
    renderApp('/ideas');
    await screen.findByRole('row', { name: 'SOLUSDT breakout-long 1h' });
    const choice = screen.getByRole('combobox', { name: 'Метка' });
    await waitFor(() =>
      expect(
        within(choice)
          .getAllByRole('option')
          .map((option) => option.textContent),
      ).toEqual(['Метка: все', '↓ BTC', '⚡ всплеск']),
    );

    await userEvent.selectOptions(choice, '⚡ всплеск');

    await waitFor(() =>
      expect(
        requests
          .map((request) => new URL(request.url))
          .filter((url) => url.pathname === '/api/ideas')
          .at(-1)!
          .searchParams.get('label'),
      ).toBe('⚡ всплеск'),
    );
  });
});
