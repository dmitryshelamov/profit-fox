import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { clearComparison, toggleComparison } from '@/modules/comparison';
import { fakeApi, json } from '@/test/fakeApi';
import {
  backtestFull,
  backtestId,
  calibrationOf,
  filterFields,
  ideasPage,
  marketSnapshot,
  resultOf,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

describe('routes', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('opens the strategy list at the root', async () => {
    fakeApi({ 'GET /api/strategies': () => json([]) });

    const { history } = renderApp('/');

    await waitFor(() => expect(history.location.pathname).toBe('/strategies'));
    expect(await screen.findByRole('heading', { name: 'Стратегии' })).toBeInTheDocument();
  });

  it('shows "not found" for an unknown path', async () => {
    fakeApi({});

    renderApp('/nope');

    expect(await screen.findByRole('heading', { name: 'Страница не найдена' })).toBeInTheDocument();
  });

  it('leaves room under the page for the comparison bar while it is shown', async () => {
    sessionStorage.clear();
    clearComparison();
    fakeApi({ 'GET /api/strategies': () => json([]) });
    renderApp('/strategies');

    await screen.findByRole('heading', { name: 'Стратегии' });
    expect(screen.getByRole('main')).not.toHaveClass('pb-28');

    act(() => toggleComparison({ backtestId, combination: 1 }));

    expect(screen.getByRole('main')).toHaveClass('pb-28');
    act(() => clearComparison());
  });
});

describe('bot section', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Переход в раздел".
  it('opens the bot list from the header and marks it as current', async () => {
    fakeApi({ 'GET /api/strategies': () => json([]), 'GET /api/bots': () => json([]) });

    const { history } = renderApp('/strategies');

    await userEvent.click(await screen.findByRole('link', { name: 'Боты' }));

    await waitFor(() => expect(history.location.pathname).toBe('/bots'));
    expect(await screen.findByRole('heading', { name: 'Боты' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Боты' })).toHaveAttribute('aria-current', 'page');
  });

  const id = '5d9e4a52-0000-4000-8000-000000000001';

  it.each([
    [`/backtests/${id}`, 'Бэктест'],
    [`/backtests/${id}/results/7`, 'Комбинация #7'],
    [`/bots/${id}/backtests/new`, 'Новый бэктест'],
    ['/compare?items=a:1,b:2', 'Сравнение'],
  ])('opens a direct link %s in the shell', async (path, heading) => {
    fakeApi({
      [`GET /api/backtests/${id}`]: () => json(backtestFull({ id })),
      [`GET /api/backtests/${id}/results/7`]: () => json(resultOf(7)),
    });

    renderApp(path);

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Разделы' })).toBeInTheDocument();
  });
});

describe('menu', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Раздел «Рынок»".
  it('lists the sections with the market between composite nodes and bots', async () => {
    fakeApi({ 'GET /api/strategies': () => json([]) });
    renderApp('/strategies');

    const menu = await screen.findByRole('navigation', { name: 'Разделы' });

    expect(
      within(menu)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Стратегии', 'Составные узлы', 'Рынок', 'Идеи', 'Журнал', 'Боты', 'Аккаунты']);
  });

  it('opens the market from the menu and marks it as current', async () => {
    fakeApi({
      'GET /api/strategies': () => json([]),
      'GET /api/market': () => json(marketSnapshot([], { snapshotAt: null })),
    });
    const { history } = renderApp('/strategies');

    await userEvent.click(await screen.findByRole('link', { name: 'Рынок' }));

    await waitFor(() => expect(history.location.pathname).toBe('/market'));
    expect(screen.getByRole('link', { name: 'Рынок' })).toHaveAttribute('aria-current', 'page');
  });
});

describe('the page of a list in the address', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // The API answers with the page it was asked for, so the address is not moved.
  const open = () =>
    fakeApi({
      'GET /api/ideas': (request) =>
        json(ideasPage([], { page: Number(new URL(request.url).searchParams.get('Page')) })),
      'GET /api/market': (request) =>
        json(marketSnapshot([], { page: Number(new URL(request.url).searchParams.get('Page')) })),
      'GET /api/saved-filters': () => json([]),
      'GET /api/saved-filters/fields': () => json(filterFields),
    });
  const pageAsked = (requests: Request[], path: string) =>
    new URL(
      requests.find((request) => new URL(request.url).pathname === path)!.url,
    ).searchParams.get('Page');

  it.each(['/ideas', '/market'])('reads ?page=3 of %s as the third page', async (path) => {
    const requests = open();

    renderApp(`${path}?page=3`);

    await waitFor(() => expect(pageAsked(requests, `/api${path}`)).toBe('3'));
  });

  it.each(['/ideas', '/market'])(
    'reads ?page=0 and ?page=x of %s as the first page',
    async (path) => {
      for (const page of ['0', 'x', '-2', '1.5']) {
        const requests = open();
        const { unmount } = renderApp(`${path}?page=${page}`);

        await waitFor(() => expect(pageAsked(requests, `/api${path}`), page).toBe('1'));
        unmount();
      }
    },
  );
});

describe('the calibration of the ideas', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const asked = (requests: Request[]) =>
    new URL(
      requests.find((request) => new URL(request.url).pathname === '/api/ideas/calibration')!.url,
    ).searchParams.toString();

  it('reads the window and the category of the address and keeps «Идеи» current', async () => {
    const requests = fakeApi({ 'GET /api/ideas/calibration': () => json(calibrationOf()) });

    renderApp('/ideas/calibration?days=30&category=spot');

    expect(await screen.findByRole('heading', { name: 'Калибровка' })).toBeInTheDocument();
    await waitFor(() => expect(asked(requests)).toBe('Days=30&Category=spot'));
    expect(
      within(screen.getByRole('navigation', { name: 'Разделы' })).getByRole('link', {
        name: 'Идеи',
      }),
    ).toHaveAttribute('aria-current', 'page');
  });

  it('reads another window or category of the address as 90 days of all categories', async () => {
    const requests = fakeApi({ 'GET /api/ideas/calibration': () => json(calibrationOf()) });

    renderApp('/ideas/calibration?days=60&category=inverse');

    await waitFor(() => expect(asked(requests)).toBe('Days=90'));
  });
});
