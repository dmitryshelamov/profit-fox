import { screen, within } from '@testing-library/react';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { botSummary, emaSummary, ladderSummary, progressOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

describe('bot list', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function open(bots: unknown[]) {
    fakeApi({
      'GET /api/bots': () => json(bots),
      'GET /api/strategies': () => json([emaSummary, ladderSummary]),
    });

    renderApp('/bots');
  }

  it('shows the bot with its strategy, instrument, interval and balance', async () => {
    open([{ ...botSummary, balance: '1000' }]);

    const row = (await screen.findByText('BTC EMA час')).closest('tr')!;
    expect(await within(row).findByText('EMA')).toBeInTheDocument();
    expect(within(row).getByText('linear · BTCUSDT')).toBeInTheDocument();
    expect(within(row).getByText('1 ч')).toBeInTheDocument();
    expect(within(row).getByText('1 000')).toBeInTheDocument();
  });

  // Scenario "Последний бэктест считается".
  it('shows the status and progress of the last backtest that is running', async () => {
    open([
      {
        ...botSummary,
        lastBacktest: { id: 'b1', status: 'Running', progress: progressOf(5, 12) },
      },
    ]);

    const row = (await screen.findByText('BTC EMA час')).closest('tr')!;
    expect(within(row).getByText('Считается · 5 / 12')).toBeInTheDocument();
  });

  it('shows how many combinations a finished backtest calculated', async () => {
    open([
      {
        ...botSummary,
        lastBacktest: { id: 'b1', status: 'Succeeded', progress: progressOf(240, 240) },
      },
    ]);

    const row = (await screen.findByText('BTC EMA час')).closest('tr')!;
    expect(within(row).getByText('Готов')).toBeInTheDocument();
    expect(within(row).getByText('240 комбинаций')).toBeInTheDocument();
  });

  // Scenario "Бот без бэктестов".
  it('marks a bot without backtests', async () => {
    open([{ ...botSummary, name: 'BTC DCA день' }]);

    const row = (await screen.findByText('BTC DCA день')).closest('tr')!;
    expect(within(row).getByText('Нет бэктестов')).toBeInTheDocument();
  });

  it('opens a new bot form from the list', async () => {
    open([]);

    expect(await screen.findByRole('link', { name: '+ Новый бот' })).toHaveAttribute(
      'href',
      '/bots/new',
    );
  });

  it('says when the bots cannot be loaded', async () => {
    fakeApi({
      'GET /api/bots': () => problem(404, 'нет'),
      'GET /api/strategies': () => json([emaSummary]),
    });
    renderApp('/bots');

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить ботов.');
  });
});
