import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { botId, botSummary, testnetAccount } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const path = `/accounts/${testnetAccount.id}`;
const accountUrl = `GET /api/exchange-accounts/${testnetAccount.id}`;
const balanceUrl = `GET /api/exchange-accounts/${testnetAccount.id}/balance`;
const details = { ...testnetAccount, bots: [{ id: botId, name: 'BTC EMA час' }] };
const balance = {
  totalEquityUsd: '1675.31',
  coins: [
    { coin: 'USDT', walletBalance: '1000.123456', usdValue: '1000.25' },
    { coin: 'BTC', walletBalance: '0.01', usdValue: '675.06' },
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

    expect(await screen.findByText('XKz8apikeyTESTNET')).toBeInTheDocument();
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

    await screen.findByText('XKz8apikeyTESTNET');
    expect(requests.some((request) => request.url.endsWith('/balance'))).toBe(false);

    await userEvent.click(screen.getByRole('button', { name: 'Показать баланс' }));

    const table = await screen.findByRole('table', { name: 'Баланс' });
    expect(within(table).getByText('USDT')).toBeInTheDocument();
    expect(within(table).getByText('1 000.123456')).toBeInTheDocument();
    expect(within(table).getByText('1 675.31')).toBeInTheDocument();
  });

  // Scenario "Биржа недоступна при чтении баланса".
  it('says that bybit is unavailable and stays usable', async () => {
    fakeApi({
      [accountUrl]: () => json(details),
      [balanceUrl]: () => problem(503, 'Bybit is unavailable.'),
    });
    renderApp(path);

    await userEvent.click(await screen.findByRole('button', { name: 'Показать баланс' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Bybit недоступен, попробуйте позже.',
    );
    expect(screen.getByRole('button', { name: 'Показать баланс' })).toBeEnabled();
  });

  // Scenario "Новое имя".
  it('renames the account', async () => {
    let name = 'Проверка';
    const requests = fakeApi({
      [accountUrl]: () => json({ ...details, name }),
      [`PUT /api/exchange-accounts/${testnetAccount.id}`]: async (request) => {
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
      [`PUT /api/exchange-accounts/${testnetAccount.id}`]: async (request) => {
        name = ((await request.json()) as { name: string }).name;

        return new Response(null, { status: 204 });
      },
      'GET /api/bots': () =>
        json([
          {
            ...botSummary,
            exchangeAccount: { id: testnetAccount.id, name, environment: 'testnet' },
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
      [`PUT /api/exchange-accounts/${testnetAccount.id}`]: () =>
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
      [`PUT /api/exchange-accounts/${testnetAccount.id}`]: () => problem(500, 'Server error.'),
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
      [`DELETE /api/exchange-accounts/${testnetAccount.id}`]: () =>
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
      [`DELETE /api/exchange-accounts/${testnetAccount.id}`]: () =>
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
