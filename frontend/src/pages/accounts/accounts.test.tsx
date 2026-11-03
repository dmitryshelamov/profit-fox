import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { exchangeAccount } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

describe('account list', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Переход в раздел".
  it('opens from the header and marks the section current', async () => {
    fakeApi({
      'GET /api/exchange-accounts': () => json([]),
      'GET /api/strategies': () => json([]),
    });
    renderApp('/strategies');

    await userEvent.click(await screen.findByRole('link', { name: 'Аккаунты' }));

    expect(await screen.findByRole('heading', { name: 'Аккаунты' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Аккаунты' })).toHaveAttribute('aria-current', 'page');
  });

  // Scenario "Аккаунт в списке".
  it('shows each account with its key, uid and categories', async () => {
    fakeApi({ 'GET /api/exchange-accounts': () => json([exchangeAccount]) });
    renderApp('/accounts');

    const row = (await screen.findByText('Проверка')).closest('tr')!;
    expect(within(row).getByText('XKz8apikeyMAIN')).toBeInTheDocument();
    expect(within(row).getByText('24617703')).toBeInTheDocument();
    expect(within(row).getByText('spot, linear')).toBeInTheDocument();
    expect(within(row).queryByText('Testnet')).not.toBeInTheDocument();
  });

  it('offers to add the first account', async () => {
    fakeApi({ 'GET /api/exchange-accounts': () => json([]) });
    renderApp('/accounts');

    expect(await screen.findByRole('link', { name: 'Добавить аккаунт' })).toHaveAttribute(
      'href',
      '/accounts/new',
    );
  });
});
