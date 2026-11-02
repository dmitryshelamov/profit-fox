import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { clearComparison, toggleComparison } from '@/modules/comparison';
import { fakeApi, json } from '@/test/fakeApi';
import { backtestFull, backtestId, resultOf } from '@/test/fixtures';
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
