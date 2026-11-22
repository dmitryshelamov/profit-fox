import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { openFundingHigh } from '@/test/editor';
import { fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

const run = {
  hypothesis: 'H0001',
  version: 1,
  period: 'exploration',
  kind: 'verdict',
  queuedAt: '2026-11-21T00:10:00Z',
  startedAt: null,
  finishedAt: null,
  worker: null,
  pairsDone: 0,
  pairsTotal: null,
  lostLeases: 0,
  cancelRequested: false,
  result: null,
};
const going = { ...run, id: 'r1', state: 'running', worker: 'soyo-02', pairsDone: 3 };
const waiting = { ...run, id: 'r2', state: 'queued', kind: 'parity' };

const activeRequests = (requests: readonly Request[]) =>
  requests.filter((request) => {
    const url = new URL(request.url);
    return url.pathname === '/api/runs' && url.searchParams.get('active') === 'true';
  });

// Spec bench-ui «Общая шапка».
describe('the header', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Текущий раздел».
  it('has the three sections and marks «Прогоны» as current on the page of a run', async () => {
    fakeApi({
      'GET /api/runs': () => json([]),
      'GET /api/runs/r5': () => json({ ...going, id: 'r5' }),
    });
    renderApp('/runs/r5');

    const sections = within(await screen.findByRole('navigation', { name: 'Разделы' }));
    expect(sections.getAllByRole('link').map((link) => link.textContent)).toEqual([
      'Сигналы',
      'Стратегии',
      'Прогоны',
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
  ])('marks the section of %s as current', async (path, section) => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/strategies': () => json([]),
      'GET /api/runs': () => json([]),
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
    fakeApi({ ...baseRoutes(), 'GET /api/runs': () => json([going, waiting]) });
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
    fakeApi({ ...baseRoutes(), 'GET /api/runs': () => json(runs) });
    renderApp('/signals');

    expect(await screen.findByRole('link', { name: words })).toBeInTheDocument();
  });

  // Scenario «Очередь пуста».
  it('says that the queue is empty', async () => {
    fakeApi({ ...baseRoutes(), 'GET /api/runs': () => json([]) });
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
      'GET /api/runs': () => json({ status: 500, title: 'the database is down' }, 500),
    });
    renderApp('/signals');

    const state = await screen.findByRole('link', { name: 'Стенд не отвечает' }, { timeout: 4000 });
    expect(state).toHaveAttribute('data-state', 'error');
    expect(state.textContent).not.toMatch(/\d/);
  });

  it('asks for the queue once on the page «Прогоны», for the header and the list together', async () => {
    const requests = fakeApi({
      'GET /api/runs': (request) =>
        json(new URL(request.url).searchParams.get('active') === 'true' ? [going, waiting] : []),
    });
    renderApp('/runs');

    await screen.findByRole('link', { name: 'Идёт 1 · ждёт 1' });
    await screen.findByRole('listitem', { name: 'Прогон H0001 r1' });
    expect(activeRequests(requests)).toHaveLength(1);
  });

  it('follows the queue without a reload: every 3 s while runs are there, every 30 s when it is empty', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let runs: unknown[] = [going];
    const requests = fakeApi({ ...baseRoutes(), 'GET /api/runs': () => json(runs) });
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
