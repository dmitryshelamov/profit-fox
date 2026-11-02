import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import {
  backtestSummary,
  botDetails,
  botId,
  botSummary,
  emaDefinition,
  emaSummary,
  progressOf,
  savedStrategy,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

function routes(backtests: () => unknown[]) {
  return {
    [`GET /api/bots/${botId}`]: () => json(botDetails),
    [`GET /api/bots/${botId}/backtests`]: () => json(backtests()),
    'GET /api/strategies': () => json([emaSummary]),
    [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, emaDefinition)),
    'GET /api/market-data/instruments': () =>
      json([{ category: 'linear', symbol: 'BTCUSDT', baseCoin: 'BTC', quoteCoin: 'USDT' }]),
  };
}

describe('bot page', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('shows the settings of the bot with the quote coin of the balance', async () => {
    fakeApi(routes(() => []));
    renderApp(`/bots/${botId}`);

    expect(await screen.findByRole('heading', { name: 'BTC EMA час' })).toBeInTheDocument();
    expect(
      await screen.findByText('EMA · linear · BTCUSDT · 1 ч · баланс 1 000 USDT'),
    ).toBeInTheDocument();
  });

  // Scenario "Значения параметров".
  it('marks the parameters that take their default', async () => {
    fakeApi(routes(() => []));
    renderApp(`/bots/${botId}`);

    const parameters = await screen.findByRole('region', { name: 'Параметры' });
    const fast = (await within(parameters).findByText('fast')).closest('li')!;
    expect(within(fast).getByText('10')).toBeInTheDocument();
    expect(within(fast).queryByText('по умолчанию')).not.toBeInTheDocument();
    const slow = within(parameters).getByText('slow').closest('li')!;
    expect(within(slow).getByText('26')).toBeInTheDocument();
    expect(within(slow).getByText('по умолчанию')).toBeInTheDocument();
  });

  it('lists backtests with period, ranges and combinations', async () => {
    fakeApi(
      routes(() => [
        backtestSummary({
          parameters: { fast: { min: 5, max: 15, step: 5 } },
          progress: progressOf(3, 3),
        }),
      ]),
    );
    renderApp(`/bots/${botId}`);

    const row = (await screen.findByText('02.11 14:05')).closest('tr')!;
    expect(within(row).getByText('01.01.2024 – 01.07.2024')).toBeInTheDocument();
    expect(within(row).getByText('fast 5…15 / 5')).toBeInTheDocument();
    expect(within(row).getByText('3')).toBeInTheDocument();
    expect(within(row).getByText('Готов')).toBeInTheDocument();
  });

  it('shows a dash where no parameter is a range', async () => {
    fakeApi(routes(() => [backtestSummary()]));
    renderApp(`/bots/${botId}`);

    const row = (await screen.findByText('02.11 14:05')).closest('tr')!;
    expect(within(row).getByText('—')).toBeInTheDocument();
  });

  // Scenario "Ошибка бэктеста".
  it('shows the reason a backtest failed', async () => {
    fakeApi(
      routes(() => [backtestSummary({ status: 'Failed', error: 'Bybit: биржа недоступна' })]),
    );
    renderApp(`/bots/${botId}`);

    const row = (await screen.findByText('02.11 14:05')).closest('tr')!;
    expect(within(row).getByText('Ошибка')).toBeInTheDocument();
    expect(within(row).getByText('Bybit: биржа недоступна')).toBeInTheDocument();
  });

  it('shows how many combinations a cancelled backtest calculated', async () => {
    fakeApi(routes(() => [backtestSummary({ status: 'Cancelled', progress: progressOf(7, 19) })]));
    renderApp(`/bots/${botId}`);

    expect(await screen.findByText('Отменён · 7 / 19')).toBeInTheDocument();
  });

  // Scenario "Бот изменился".
  it('marks a backtest that was run for another bot', async () => {
    fakeApi(
      routes(() => [
        backtestSummary({ botChanged: true }),
        backtestSummary({ id: 'other', createdAt: '2026-11-01T10:00:00Z' }),
      ]),
    );
    renderApp(`/bots/${botId}`);

    const changed = (await screen.findByText('02.11 14:05')).closest('tr')!;
    expect(within(changed).getByText('бот изменился')).toBeInTheDocument();
    const other = screen.getByText('01.11 10:00').closest('tr')!;
    expect(within(other).queryByText('бот изменился')).not.toBeInTheDocument();
  });

  // Scenario "Ход расчёта обновляется".
  it('polls every 2 seconds while a backtest is unfinished and stops when it is done', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let calculated = 5;
    let status = 'Running';
    const requests = fakeApi(
      routes(() => [backtestSummary({ status, progress: progressOf(calculated, 9) })]),
    );
    renderApp(`/bots/${botId}`);

    expect(await screen.findByText('Считается · 5 / 9')).toBeInTheDocument();
    calculated = 7;
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('Считается · 7 / 9')).toBeInTheDocument();

    status = 'Succeeded';
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('Готов')).toBeInTheDocument();

    const listed = () => requests.filter((request) => request.url.endsWith('/backtests')).length;
    const before = listed();
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(listed()).toBe(before);
  });

  // Design decision 3: a finished backtest changes what the bots list shows.
  it('refreshes the bots list and the backtests of the bot when a backtest finishes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let status = 'Running';
    const requests = fakeApi({
      ...routes(() => [backtestSummary({ status, progress: progressOf(5, 9) })]),
      'GET /api/bots': () => json([botSummary]),
    });
    const { history } = renderApp('/bots');
    await screen.findByText('BTC EMA час');
    act(() => history.push(`/bots/${botId}`));
    expect(await screen.findByText('Считается · 5 / 9')).toBeInTheDocument();

    status = 'Succeeded';
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('Готов')).toBeInTheDocument();
    const listed = () => requests.filter((request) => request.url.endsWith('/api/bots')).length;
    const before = listed();
    act(() => history.push('/bots'));

    await waitFor(() => expect(listed()).toBe(before + 1));
  });

  it('shows a progress bar in the row of a running backtest', async () => {
    fakeApi(routes(() => [backtestSummary({ status: 'Running', progress: progressOf(5, 10) })]));
    renderApp(`/bots/${botId}`);

    const bar = await screen.findByRole('progressbar');
    expect(bar).toHaveAttribute('aria-valuenow', '50');
  });

  // Scenario "Подтверждённое удаление".
  it('deletes the bot after the confirmation and opens the list', async () => {
    let deleted = false;
    const requests = fakeApi({
      ...routes(() => [backtestSummary(), backtestSummary({ id: 'second' })]),
      [`DELETE /api/bots/${botId}`]: () => {
        deleted = true;

        return new Response(null, { status: 204 });
      },
      'GET /api/bots': () => json(deleted ? [] : [botDetails]),
    });
    const { history } = renderApp(`/bots/${botId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Действия: BTC EMA час' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить бота' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Вместе с ботом удаляются все его бэктесты.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Удалить' }));

    await waitFor(() => expect(history.location.pathname).toBe('/bots'));
    expect(requests.some((request) => request.method === 'DELETE')).toBe(true);
    expect(await screen.findByRole('heading', { name: 'Боты' })).toBeInTheDocument();
    expect(screen.queryByText('BTC EMA час')).not.toBeInTheDocument();
  });

  it('keeps the bot when the deletion is declined', async () => {
    const requests = fakeApi(routes(() => []));
    renderApp(`/bots/${botId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Действия: BTC EMA час' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить бота' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Отмена' }),
    );

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(requests.some((request) => request.method === 'DELETE')).toBe(false);
  });

  it('says when the bot cannot be loaded', async () => {
    fakeApi({ ...routes(() => []), [`GET /api/bots/${botId}`]: () => problem(404, 'нет') });
    renderApp(`/bots/${botId}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить бота.');
  });

  it('says when the backtests cannot be loaded and still shows the bot', async () => {
    fakeApi({
      ...routes(() => []),
      [`GET /api/bots/${botId}/backtests`]: () => problem(404, 'нет'),
    });
    renderApp(`/bots/${botId}`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить бэктесты.');
    expect(screen.getByRole('heading', { name: 'BTC EMA час' })).toBeInTheDocument();
  });

  it('shows why the bot was not deleted and stays on the page', async () => {
    fakeApi({
      ...routes(() => []),
      [`DELETE /api/bots/${botId}`]: () => problem(409, 'У бота есть бэктест в работе.'),
    });
    const { history } = renderApp(`/bots/${botId}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Действия: BTC EMA час' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить бота' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('У бота есть бэктест в работе.');
    expect(history.location.pathname).toBe(`/bots/${botId}`);
  });
});
