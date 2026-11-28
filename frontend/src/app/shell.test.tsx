import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { openFundingHigh } from '@/test/editor';
import { fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';
import { backtest } from '@/test/runs';
import { riskSettings, riskState } from '@/test/trades';

const going = backtest({
  id: 'r1',
  state: 'running',
  startedAt: '2026-11-21T00:11:00Z',
  worker: 'soyo-02',
  pairsDone: 3,
  pairsTotal: 828,
});
const waiting = backtest({ id: 'r2' });

const activeRequests = (requests: readonly Request[]) =>
  requests.filter((request) => {
    const url = new URL(request.url);
    return url.pathname === '/api/backtests' && url.searchParams.get('active') === 'true';
  });

// Spec bench-ui «Общая шапка».
describe('the header', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Текущий раздел»: nine sections since change take-idea-trades.
  it('has the nine sections and marks «Прогоны» as current on the page of a run', async () => {
    fakeApi({
      'GET /api/backtests': () => json([]),
      'GET /api/backtests/r5': () => json({ ...going, id: 'r5' }),
    });
    renderApp('/runs/r5');

    const sections = within(await screen.findByRole('navigation', { name: 'Разделы' }));
    expect(sections.getAllByRole('link').map((link) => link.textContent)).toEqual([
      'Сигналы',
      'Стратегии',
      'Прогоны',
      'Идеи',
      'Сделки',
      'Риск',
      'Рынок',
      'Скоринг',
      'Аккаунты',
    ]);
    await waitFor(() =>
      expect(
        sections
          .getAllByRole('link')
          .filter((link) => link.getAttribute('aria-current') === 'page')
          .map((link) => link.textContent),
      ).toEqual(['Прогоны']),
    );
  });

  it.each([
    ['/signals', 'Сигналы'],
    ['/strategies', 'Стратегии'],
    ['/runs', 'Прогоны'],
    ['/ideas', 'Идеи'],
    ['/market', 'Рынок'],
    // Scenario «Страница пары».
    ['/market/SOLUSDT', 'Рынок'],
    // Change add-idea-score, scenario «Страница «Скоринг»».
    ['/scoring', 'Скоринг'],
    // Change add-exchange-accounts, scenario «Страница «Аккаунты»».
    ['/accounts', 'Аккаунты'],
    // Change take-idea-trades, scenarios «Страница «Сделки»» and «Страница «Риск»».
    ['/trades', 'Сделки'],
    ['/risk', 'Риск'],
  ])('marks the section of %s as current', async (path, section) => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/accounts': () => json([]),
      'GET /api/trades': () =>
        json({ items: [], total: 0, page: 1, pageSize: 50, open: 0, closed: 0 }),
      'GET /api/risk/state': () => json(riskState()),
      'GET /api/risk/settings': () => json(riskSettings()),
      'GET /api/strategies': () => json([]),
      'GET /api/backtests': () => json([]),
      'GET /api/scoring/labels': () => json([]),
      'GET /api/ideas/scores': () => json({ rows: [], filterGone: false }),
    });
    renderApp(path);

    const sections = within(await screen.findByRole('navigation', { name: 'Разделы' }));
    await waitFor(() =>
      expect(sections.getByRole('link', { name: section })).toHaveAttribute('aria-current', 'page'),
    );
    expect(
      sections.getAllByRole('link').filter((link) => link.hasAttribute('aria-current')),
    ).toHaveLength(1);
  });

  // Scenario «Редактор без шапки».
  it('is not on the editor of a signal, whose bar leads back to the list of signals', async () => {
    openFundingHigh();

    await screen.findByRole('status', { name: 'Состояние' });
    expect(screen.queryByRole('navigation', { name: 'Разделы' })).toBeNull();
    expect(screen.getByRole('link', { name: /Сигналы/ })).toHaveAttribute('href', '/signals');
  });
});

// Spec bench-ui «Состояние очереди в шапке».
describe('the state of the queue in the header', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario «Идёт и ждёт».
  it('says how many runs go and wait, and opens «Прогоны» by a click on these words', async () => {
    fakeApi({ ...baseRoutes(), 'GET /api/backtests': () => json([going, waiting]) });
    const { history } = renderApp('/signals');

    const state = await screen.findByRole('link', { name: 'Идёт 1 · ждёт 1' });
    expect(state.closest('header')).toContainElement(
      screen.getByRole('navigation', { name: 'Разделы' }),
    );
    await userEvent.click(state);

    expect(await screen.findByRole('heading', { name: 'Прогоны' })).toBeInTheDocument();
    expect(history.location.pathname).toBe('/runs');
  });

  it.each([
    [[going], 'Идёт 1'],
    [[waiting, { ...waiting, id: 'r3' }], 'Ждёт 2'],
  ])('names only what there is', async (runs, words) => {
    fakeApi({ ...baseRoutes(), 'GET /api/backtests': () => json(runs) });
    renderApp('/signals');

    expect(await screen.findByRole('link', { name: words })).toBeInTheDocument();
  });

  // Scenario «Очередь пуста».
  it('says that the queue is empty', async () => {
    fakeApi({ ...baseRoutes(), 'GET /api/backtests': () => json([]) });
    renderApp('/signals');

    expect(await screen.findByRole('link', { name: 'Очередь пуста' })).toHaveAttribute(
      'href',
      '/runs',
    );
  });

  // Scenario «API не отвечает».
  it('says that the bench does not answer instead of the numbers', async () => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/backtests': () => json({ status: 500, title: 'the database is down' }, 500),
    });
    renderApp('/signals');

    const state = await screen.findByRole('link', { name: 'Стенд не отвечает' }, { timeout: 4000 });
    expect(state).toHaveAttribute('data-state', 'error');
    expect(state.textContent).not.toMatch(/\d/);
  });

  it('asks for the queue once on the page «Прогоны», for the header and the list together', async () => {
    const requests = fakeApi({
      'GET /api/backtests': (request) =>
        json(new URL(request.url).searchParams.get('active') === 'true' ? [going, waiting] : []),
    });
    renderApp('/runs');

    await screen.findByRole('link', { name: 'Идёт 1 · ждёт 1' });
    await screen.findByRole('listitem', { name: 'Прогон funding-short r1' });
    expect(activeRequests(requests)).toHaveLength(1);
  });

  it('follows the queue without a reload: every 3 s while runs are there, every 30 s when it is empty', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let runs: unknown[] = [going];
    const requests = fakeApi({ ...baseRoutes(), 'GET /api/backtests': () => json(runs) });
    renderApp('/signals');
    await screen.findByRole('link', { name: 'Идёт 1' });

    runs = [];
    await act(() => vi.advanceTimersByTimeAsync(3100));
    expect(await screen.findByRole('link', { name: 'Очередь пуста' })).toBeInTheDocument();

    runs = [waiting];
    const asked = activeRequests(requests).length;
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(activeRequests(requests)).toHaveLength(asked);
    await act(() => vi.advanceTimersByTimeAsync(20_100));
    expect(await screen.findByRole('link', { name: 'Ждёт 1' })).toBeInTheDocument();
  });
});
