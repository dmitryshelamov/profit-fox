import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { brokerAccounts, exchangeAccount, tinvestAccount } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const details = { ...exchangeAccount, bots: [] };
const brokerAccountsUrl = 'POST /api/exchange-accounts/tinvest/accounts';

async function fillTInvest() {
  await userEvent.click(await screen.findByRole('button', { name: 'T-Invest' }));
  await userEvent.type(screen.getByLabelText('Название'), 'Брокерский');
  await userEvent.type(screen.getByLabelText('Токен'), 't.token-value');
  await userEvent.click(screen.getByRole('button', { name: 'Показать счета' }));
}

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
      exchange: 'bybit',
      apiKey: 'XKz8apikeyMAIN',
      apiSecret: 'secret-value',
    });
  });

  // Scenario "Смена биржи в форме".
  it('starts with bybit and shows the token and the accounts of t-invest after the switch', async () => {
    fakeApi({});
    renderApp('/accounts/new');

    expect(await screen.findByRole('button', { name: 'Bybit' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByLabelText('API key')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'T-Invest' }));

    expect(screen.getByLabelText('Токен')).toHaveAttribute('type', 'password');
    expect(screen.getByRole('button', { name: 'Показать счета' })).toBeInTheDocument();
    expect(screen.queryByLabelText('API key')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('API secret')).not.toBeInTheDocument();
  });

  // Scenarios "Счёт выбран и сохранён" and "Недоступный счёт".
  it('lists the accounts of the token, lets only an available one be chosen and saves it', async () => {
    const requests = fakeApi({
      [brokerAccountsUrl]: () => json(brokerAccounts),
      'POST /api/exchange-accounts': () => json({ id: tinvestAccount.id }, 201),
      [`GET /api/exchange-accounts/${tinvestAccount.id}`]: () =>
        json({ ...tinvestAccount, bots: [] }),
      'GET /api/exchange-accounts': () => json([tinvestAccount]),
    });
    const { history } = renderApp('/accounts/new');

    await fillTInvest();

    const brokerage = await screen.findByRole('radio', { name: /2000000001/ });
    const iis = screen.getByRole('radio', { name: /2000000002/ });
    expect(iis).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Добавить аккаунт' })).toBeDisabled();
    expect(screen.getByText('не открыт')).toBeInTheDocument();
    expect(screen.getByText('Брокерский счёт')).toBeInTheDocument();
    const asked = requests.find((request) => request.url.endsWith('/tinvest/accounts'))!;
    expect(await asked.json()).toEqual({ token: 't.token-value' });

    await userEvent.click(brokerage);
    await userEvent.click(screen.getByRole('button', { name: 'Добавить аккаунт' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/accounts/${tinvestAccount.id}`));
    const post = requests.find(
      (request) => request.method === 'POST' && request.url.endsWith('/api/exchange-accounts'),
    )!;
    expect(await post.json()).toEqual({
      name: 'Брокерский',
      exchange: 'tinvest',
      token: 't.token-value',
      accountId: '2000000001',
    });
  });

  // After the review: a late answer of a listing must not reach a form that moved on, so nothing can move it on while the answer is awaited.
  it('locks the exchange and the token while the accounts are read', async () => {
    let answer!: (value: Response) => void;
    fakeApi({
      [brokerAccountsUrl]: () =>
        new Promise<Response>((resolve) => {
          answer = resolve;
        }),
    });
    renderApp('/accounts/new');

    await fillTInvest();

    expect(screen.getByRole('button', { name: 'Bybit' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'T-Invest' })).toBeDisabled();
    expect(screen.getByLabelText('Токен')).toBeDisabled();
    answer(json(brokerAccounts));
    expect(await screen.findByRole('radio', { name: /2000000001/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bybit' })).toBeEnabled();
    expect(screen.getByLabelText('Токен')).toBeEnabled();
  });

  it('locks the exchange and the token while the account is saved', async () => {
    let answer!: (value: Response) => void;
    fakeApi({
      [brokerAccountsUrl]: () => json(brokerAccounts),
      'POST /api/exchange-accounts': () =>
        new Promise<Response>((resolve) => {
          answer = resolve;
        }),
    });
    renderApp('/accounts/new');
    await fillTInvest();
    await userEvent.click(await screen.findByRole('radio', { name: /2000000001/ }));

    await userEvent.click(screen.getByRole('button', { name: 'Добавить аккаунт' }));

    expect(screen.getByRole('button', { name: 'Bybit' })).toBeDisabled();
    expect(screen.getByLabelText('Токен')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Показать счета' })).toBeDisabled();
    answer(problem(400, 'x', { errors: { accountId: ['The account is closed.'] } }));
    expect(await screen.findByText('The account is closed.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bybit' })).toBeEnabled();
  });

  // The accounts were read with the old token: a changed token has none until it is read again.
  it('drops the accounts and the chosen one when the token changes', async () => {
    fakeApi({ [brokerAccountsUrl]: () => json(brokerAccounts) });
    renderApp('/accounts/new');
    await fillTInvest();
    await userEvent.click(await screen.findByRole('radio', { name: /2000000001/ }));
    expect(screen.getByRole('button', { name: 'Добавить аккаунт' })).toBeEnabled();

    await userEvent.type(screen.getByLabelText('Токен'), '2');

    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Добавить аккаунт' })).toBeDisabled();
  });

  it('says when the token opens no account', async () => {
    fakeApi({ [brokerAccountsUrl]: () => json([]) });
    renderApp('/accounts/new');

    await fillTInvest();

    expect(await screen.findByText('У токена нет счетов.')).toBeInTheDocument();
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
  });

  it('shows the error of a rejected token at the field and clears the token', async () => {
    fakeApi({
      [brokerAccountsUrl]: () =>
        problem(400, 'x', { errors: { token: ['T-Invest does not accept the token.'] } }),
    });
    renderApp('/accounts/new');

    await fillTInvest();

    expect(await screen.findByText('T-Invest does not accept the token.')).toBeInTheDocument();
    expect(screen.getByLabelText('Токен')).toHaveValue('');
    expect(screen.getByLabelText('Название')).toHaveValue('Брокерский');
  });

  // After a refusal of the server the token is gone and the accounts with it: they were read with that token.
  it('clears the token and the accounts after the server refuses the account', async () => {
    fakeApi({
      [brokerAccountsUrl]: () => json(brokerAccounts),
      'POST /api/exchange-accounts': () =>
        problem(400, 'x', {
          errors: { accountId: ['The token has read-only access to the account.'] },
        }),
    });
    renderApp('/accounts/new');

    await fillTInvest();
    await userEvent.click(await screen.findByRole('radio', { name: /2000000001/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Добавить аккаунт' }));

    expect(
      await screen.findByText('The token has read-only access to the account.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Токен')).toHaveValue('');
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
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

  it('says that the exchange is unavailable above the buttons', async () => {
    fakeApi({ 'POST /api/exchange-accounts': () => problem(503, 'Bybit is unavailable.') });
    renderApp('/accounts/new');

    await fill();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Биржа недоступна, попробуйте позже.',
    );
  });
});
