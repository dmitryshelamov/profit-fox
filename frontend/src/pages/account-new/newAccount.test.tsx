import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { exchangeAccount } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const details = { ...exchangeAccount, bots: [] };

async function fill() {
  await userEvent.type(await screen.findByLabelText('Название'), 'Проверка');
  await userEvent.type(screen.getByLabelText('API key'), 'XKz8apikeyMAIN');
  await userEvent.type(screen.getByLabelText('API secret'), 'secret-value');
  await userEvent.click(screen.getByRole('button', { name: 'Добавить аккаунт' }));
}

describe('new account', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Аккаунт добавлен".
  it('adds an account and opens its page', async () => {
    const requests = fakeApi({
      'POST /api/exchange-accounts': () => json({ id: exchangeAccount.id }, 201),
      [`GET /api/exchange-accounts/${exchangeAccount.id}`]: () => json(details),
      'GET /api/exchange-accounts': () => json([exchangeAccount]),
    });
    const { history } = renderApp('/accounts/new');

    expect(await screen.findByLabelText('API secret')).toHaveAttribute('type', 'password');
    await fill();

    await waitFor(() => expect(history.location.pathname).toBe(`/accounts/${exchangeAccount.id}`));
    const post = requests.find((request) => request.method === 'POST')!;
    expect(await post.json()).toEqual({
      name: 'Проверка',
      apiKey: 'XKz8apikeyMAIN',
      apiSecret: 'secret-value',
    });
  });

  // Scenario "Ключ отклонён".
  it('shows the error of the key and keeps every value but the secret', async () => {
    fakeApi({
      'POST /api/exchange-accounts': () =>
        problem(400, 'x', {
          errors: { apiKey: ['Bybit does not accept the key: API key is invalid.'] },
        }),
    });
    renderApp('/accounts/new');

    await fill();

    expect(
      await screen.findByText('Bybit does not accept the key: API key is invalid.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Название')).toHaveValue('Проверка');
    expect(screen.getByLabelText('API key')).toHaveValue('XKz8apikeyMAIN');
    expect(screen.getByLabelText('API secret')).toHaveValue('');
  });

  it('says that bybit is unavailable above the buttons', async () => {
    fakeApi({ 'POST /api/exchange-accounts': () => problem(503, 'Bybit is unavailable.') });
    renderApp('/accounts/new');

    await fill();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Bybit недоступен, попробуйте позже.',
    );
  });
});
