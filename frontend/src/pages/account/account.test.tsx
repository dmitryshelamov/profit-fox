import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { botId, botSummary, exchangeAccount, tinvestAccount } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const path = `/accounts/${exchangeAccount.id}`;
const accountUrl = `GET /api/exchange-accounts/${exchangeAccount.id}`;
const balanceUrl = `GET /api/exchange-accounts/${exchangeAccount.id}/balance`;
const details = { ...exchangeAccount, bots: [{ id: botId, name: 'BTC EMA час' }] };
const balance = {
  currency: 'USD',
  totalEquity: '1675.31',
  coins: [
    { coin: 'USDT', walletBalance: '1000.123456', value: '1000.25' },
    { coin: 'BTC', walletBalance: '0.01', value: '675.06' },
  ],
};
const tinvestPath = `/accounts/${tinvestAccount.id}`;
const tinvestUrl = `GET /api/exchange-accounts/${tinvestAccount.id}`;
const portfolio = {
  currency: 'RUB',
  totalEquity: '53000',
  coins: [
    { coin: 'RUB', walletBalance: '50000', value: '50000' },
    { coin: 'SBER', walletBalance: '10', value: '3000' },
  ],
};

describe('account page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Secret маской".
  it('shows the key in full, the secret as a mask and the bots of the account', async () => {
    fakeApi({ [accountUrl]: () => json(details) });
    renderApp(path);

    expect(await screen.findByText('XKz8apikeyMAIN')).toBeInTheDocument();
    expect(screen.getByText('Bybit')).toBeInTheDocument();
    expect(screen.getByLabelText('Secret скрыт')).toHaveTextContent('••••••••');
    expect(screen.getByRole('link', { name: 'BTC EMA час' })).toHaveAttribute(
      'href',
      `/bots/${botId}`,
    );
  });

  // Scenario "Баланс по действию".
  it('reads the balance only when asked', async () => {
    const requests = fakeApi({
      [accountUrl]: () => json(details),
      [balanceUrl]: () => json(balance),
    });
    renderApp(path);

    await screen.findByText('XKz8apikeyMAIN');
    expect(requests.some((request) => request.url.endsWith('/balance'))).toBe(false);

    await userEvent.click(screen.getByRole('button', { name: 'Показать баланс' }));

    const table = await screen.findByRole('table', { name: 'Баланс' });
    expect(within(table).getByText('USDT')).toBeInTheDocument();
    expect(within(table).getByText('Стоимость, USD')).toBeInTheDocument();
    expect(within(table).getByText('1 000.123456')).toBeInTheDocument();
    expect(within(table).getByText('1 675.31')).toBeInTheDocument();
  });

  // Requirement "Страница аккаунта T-Invest": the exchange, the account number, the token as a mask and the category moex.
  it('shows a t-invest account with its number, the token as a mask and moex', async () => {
    fakeApi({ [tinvestUrl]: () => json({ ...tinvestAccount, bots: [] }) });
    renderApp(tinvestPath);

    expect(await screen.findByText('2000000001')).toBeInTheDocument();
    expect(screen.getByText('T-Invest')).toBeInTheDocument();
    expect(screen.getByLabelText('Токен скрыт')).toHaveTextContent('••••••••');
    expect(screen.getByText('moex')).toBeInTheDocument();
    expect(screen.queryByText('API key')).not.toBeInTheDocument();
  });

  // Scenario "Баланс в рублях".
  it('shows the portfolio of a t-invest account in roubles', async () => {
    fakeApi({
      [tinvestUrl]: () => json({ ...tinvestAccount, bots: [] }),
      [`GET /api/exchange-accounts/${tinvestAccount.id}/balance`]: () => json(portfolio),
    });
    renderApp(tinvestPath);

    await userEvent.click(await screen.findByRole('button', { name: 'Показать баланс' }));

    const table = await screen.findByRole('table', { name: 'Баланс' });
    expect(within(table).getByText('Бумага')).toBeInTheDocument();
    expect(within(table).getByText('Количество')).toBeInTheDocument();
    expect(within(table).getByText('Стоимость, RUB')).toBeInTheDocument();
    expect(within(table).getByText('Общая стоимость')).toBeInTheDocument();
    expect(within(table).getByText('RUB')).toBeInTheDocument();
    expect(within(table).getByText('SBER')).toBeInTheDocument();
    expect(within(table).getByText('10')).toBeInTheDocument();
    expect(within(table).getByText('3 000')).toBeInTheDocument();
    expect(within(table).getByText('53 000')).toBeInTheDocument();
  });

  // Scenario "Биржа недоступна при чтении баланса".
  it('says that the exchange is unavailable and stays usable', async () => {
    fakeApi({
      [accountUrl]: () => json(details),
      [balanceUrl]: () => problem(503, 'Bybit is unavailable.'),
    });
    renderApp(path);

    await userEvent.click(await screen.findByRole('button', { name: 'Показать баланс' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Биржа недоступна, попробуйте позже.',
    );
    expect(screen.getByRole('button', { name: 'Показать баланс' })).toBeEnabled();
  });

  // Scenario "Новое имя".
  it('renames the account', async () => {
    let name = 'Проверка';
    const requests = fakeApi({
      [accountUrl]: () => json({ ...details, name }),
      [`PUT /api/exchange-accounts/${exchangeAccount.id}`]: async (request) => {
        name = ((await request.json()) as { name: string }).name;

        return new Response(null, { status: 204 });
      },
    });
    renderApp(path);

    await userEvent.click(await screen.findByRole('button', { name: 'Переименовать' }));
    await userEvent.clear(screen.getByLabelText('Название'));
    await userEvent.type(screen.getByLabelText('Название'), 'Основной');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByRole('heading', { name: /Основной/ })).toBeInTheDocument();
    expect(requests.some((request) => request.method === 'PUT')).toBe(true);
  });

  // The bots show the name of their account, so a renamed account shows up in them at once.
  it('shows the new name in the bots of the account', async () => {
    let name = 'Проверка';
    fakeApi({
      [accountUrl]: () => json({ ...details, name }),
      [`PUT /api/exchange-accounts/${exchangeAccount.id}`]: async (request) => {
        name = ((await request.json()) as { name: string }).name;

        return new Response(null, { status: 204 });
      },
      'GET /api/bots': () =>
        json([
          {
            ...botSummary,
            exchangeAccount: { id: exchangeAccount.id, name },
          },
        ]),
      'GET /api/strategies': () => json([]),
    });
    const { history } = renderApp('/bots');
    expect(await screen.findByRole('link', { name: 'Проверка' })).toBeInTheDocument();

    await act(() => history.push(path));
    await userEvent.click(await screen.findByRole('button', { name: 'Переименовать' }));
    await userEvent.clear(screen.getByLabelText('Название'));
    await userEvent.type(screen.getByLabelText('Название'), 'Основной');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
    await screen.findByRole('heading', { name: /Основной/ });
    await act(() => history.push('/bots'));

    expect(await screen.findByRole('link', { name: 'Основной' })).toBeInTheDocument();
  });

  it('shows the error of the name at the field', async () => {
    fakeApi({
      [accountUrl]: () => json(details),
      [`PUT /api/exchange-accounts/${exchangeAccount.id}`]: () =>
        problem(400, 'x', { errors: { name: ['Name must not be empty.'] } }),
    });
    renderApp(path);

    await userEvent.click(await screen.findByRole('button', { name: 'Переименовать' }));
    await userEvent.clear(screen.getByLabelText('Название'));
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByText('Name must not be empty.')).toBeInTheDocument();
  });

  it('shows a failure of the rename without field errors at the field', async () => {
    fakeApi({
      [accountUrl]: () => json(details),
      [`PUT /api/exchange-accounts/${exchangeAccount.id}`]: () => problem(500, 'Server error.'),
    });
    renderApp(path);

    await userEvent.click(await screen.findByRole('button', { name: 'Переименовать' }));
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByText('Server error.')).toBeInTheDocument();
  });

  // Scenario "Подтверждённое удаление".
  it('deletes the account after confirmation and opens the list', async () => {
    fakeApi({
      [accountUrl]: () => json({ ...details, bots: [] }),
      [`DELETE /api/exchange-accounts/${exchangeAccount.id}`]: () =>
        new Response(null, { status: 204 }),
      'GET /api/exchange-accounts': () => json([]),
    });
    const { history } = renderApp(path);

    await userEvent.click(await screen.findByRole('button', { name: 'Удалить' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    await waitFor(() => expect(history.location.pathname).toBe('/accounts'));
  });

  // Scenario "Аккаунт с ботами".
  it('names the bots that keep the account', async () => {
    fakeApi({
      [accountUrl]: () => json(details),
      [`DELETE /api/exchange-accounts/${exchangeAccount.id}`]: () =>
        problem(409, 'Bots trade with the exchange account.', { botNames: ['BTC EMA час'] }),
    });
    const { history } = renderApp(path);

    await userEvent.click(await screen.findByRole('button', { name: 'Удалить' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Аккаунт используют боты: BTC EMA час.',
    );
    expect(history.location.pathname).toBe(path);
  });
});
