import { screen, waitFor } from '@testing-library/react';

import { fakeApi, json } from '@/test/fakeApi';
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
});
