import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, type FakeRoute, json, problem } from '@/test/fakeApi';
import { entryOf, exchangeAccount, ideaOf, marketRow, tradingSettings } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const pullback = ideaOf({
  id: 'idea-pullback',
  symbol: 'SOLUSDT',
  type: 'pullback',
  entry: '168.40',
  stop: '165.00',
  target: '175.00',
});
const active = ideaOf({ id: 'idea-active', symbol: 'SOLUSDT', type: 'levels', status: 'active' });
const expired = ideaOf({
  id: 'idea-expired',
  symbol: 'SOLUSDT',
  type: 'beyond_extreme',
  status: 'expired',
});

function routes(overrides: Record<string, FakeRoute> = {}) {
  const base = '/api/market/linear/SOLUSDT';

  return {
    [`GET ${base}`]: () => json(marketRow({ symbol: 'SOLUSDT', price: '168.42' })),
    [`GET ${base}/candles`]: () => json({ interval: '60', candles: [], unavailable: false }),
    [`GET ${base}/open-interest`]: () => json([]),
    [`GET ${base}/funding`]: () => json([]),
    [`GET ${base}/ideas`]: () => json([pullback, active, expired]),
    [`GET ${base}/zones`]: () => json({ price: '168.4', zones: [] }),
    [`POST ${base}/own-idea`]: () => json({ made: false }),
    'GET /api/exchange-accounts': () => json([exchangeAccount]),
    'GET /api/idea-trading/settings': () => json(tradingSettings),
    'GET /api/ideas/idea-pullback/entry': () => json(entryOf()),
    ...overrides,
  };
}

async function openEntry() {
  const user = userEvent.setup();
  const ideas = await screen.findByRole('region', { name: 'Идеи пары' });
  await user.click((await within(ideas).findAllByRole('button', { name: 'Войти' }))[0]!);

  return { user, dialog: await screen.findByRole('dialog', { name: 'Вход по идее' }) };
}

describe('entry by an idea from the pair page', () => {
  // Requirement "Кнопка «Войти» на идее".
  it('has «Войти» on ideas that wait for entry and are active, and not on expired ones', async () => {
    fakeApi(routes());
    renderApp('/market/linear/SOLUSDT');

    const ideas = await screen.findByRole('region', { name: 'Идеи пары' });
    expect(await within(ideas).findAllByRole('button', { name: 'Войти' })).toHaveLength(2);

    await userEvent.click(within(ideas).getByRole('button', { name: 'Истёкшие · 1' }));

    expect(within(ideas).getAllByRole('button', { name: 'Войти' })).toHaveLength(2);
    expect(
      within(within(ideas).getAllByRole('row').at(-1)!).queryByRole('button', { name: 'Войти' }),
    ).toBeNull();
  });

  // Requirement "Окно входа": the account and the risk come from the settings, the numbers from the API.
  it('shows the calculation of the entry for the account and the risk of the settings', async () => {
    const requests = fakeApi(routes());
    renderApp('/market/linear/SOLUSDT');

    const { dialog } = await openEntry();

    expect(await within(dialog).findByText('168.40')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Аккаунт')).toHaveValue(exchangeAccount.id);
    expect(within(dialog).getByLabelText('Риск, USDT')).toHaveValue('1');
    expect(within(dialog).getByText('0.47')).toBeInTheDocument();
    expect(within(dialog).getByText('79.15 USDT')).toBeInTheDocument();
    expect(within(dialog).getByText('0.99 USDT')).toBeInTheDocument();
    expect(within(dialog).getByText('1.84 (без ком. 2.00)')).toBeInTheDocument();
    expect(within(dialog).getByText('1 000 USDT')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Войти' })).toBeEnabled();
    const asked = requests.find((request) => request.url.includes('/entry'))!;
    expect(new URL(asked.url).search).toBe(`?accountId=${exchangeAccount.id}&risk=1`);
  });

  it('asks again when the risk changes', async () => {
    const requests = fakeApi(routes());
    renderApp('/market/linear/SOLUSDT');
    const { user, dialog } = await openEntry();
    const risk = within(dialog).getByLabelText('Риск, USDT');
    await within(dialog).findByText('168.40');

    await user.clear(risk);
    await user.type(risk, '2');

    await waitFor(() =>
      expect(
        requests.some((request) => new URL(request.url).searchParams.get('risk') === '2'),
      ).toBe(true),
    );
  });

  // Scenario "Войти нельзя".
  it('turns «Войти» off and names the reason when the entry is refused', async () => {
    fakeApi(
      routes({
        'GET /api/ideas/idea-pullback/entry': () =>
          json(entryOf({ riskReward: '1.2', reasons: ['risk_reward_too_low'] })),
      }),
    );
    renderApp('/market/linear/SOLUSDT');

    const { dialog } = await openEntry();

    expect(
      await within(dialog).findByText('R:R после комиссий ниже минимального'),
    ).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Войти' })).toBeDisabled();
  });

  // Scenario "Сумма упёрлась в предел".
  it('says the amount hit its limit and the risk is below the asked one', async () => {
    fakeApi(
      routes({
        'GET /api/ideas/idea-pullback/entry': () =>
          json(entryOf({ amount: '200.00', risk: '0.62', cappedByAmount: true })),
      }),
    );
    renderApp('/market/linear/SOLUSDT');

    const { dialog } = await openEntry();

    expect(await within(dialog).findByText('200.00 USDT')).toBeInTheDocument();
    expect(within(dialog).getByText('0.62 USDT')).toBeInTheDocument();
    expect(within(dialog).getByText(/Сумма упёрлась в предел/)).toBeInTheDocument();
  });

  it('shows the missing account as a reason', async () => {
    fakeApi(
      routes({
        'GET /api/idea-trading/settings': () => json({ ...tradingSettings, accountId: null }),
        'GET /api/ideas/idea-pullback/entry': () =>
          json(entryOf({ accountId: null, freeMoney: null, reasons: ['no_key'] })),
      }),
    );
    renderApp('/market/linear/SOLUSDT');

    const { dialog } = await openEntry();

    expect(
      await within(dialog).findByText('У аккаунта нет рабочего ключа: выберите аккаунт'),
    ).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Аккаунт')).toHaveValue('');
  });

  // Requirement "Окно входа": the entry creates the trade and leads to the screen of trades.
  it('creates the trade and leads to «Сделки»', async () => {
    const requests = fakeApi(
      routes({
        'POST /api/idea-trades': () => json({ id: 'trade-1' }, 201),
        'GET /api/idea-trades': () => json([]),
      }),
    );
    const { history } = renderApp('/market/linear/SOLUSDT');
    const { user, dialog } = await openEntry();
    await within(dialog).findByText('168.40');

    await user.click(within(dialog).getByRole('button', { name: 'Войти' }));

    await waitFor(() => expect(history.location.pathname).toBe('/idea-trades'));
    const created = requests.find(
      (request) => request.method === 'POST' && request.url.endsWith('/api/idea-trades'),
    )!;
    expect(await created.text()).toBe(
      `{"ideaId":"idea-pullback","accountId":"${exchangeAccount.id}","risk":1}`,
    );
  });

  it('shows the refusal of the API in the window and stays', async () => {
    fakeApi(
      routes({
        'POST /api/idea-trades': () =>
          problem(409, 'The entry is refused: too_many_open_trades.', {
            reasons: ['too_many_open_trades'],
          }),
      }),
    );
    const { history } = renderApp('/market/linear/SOLUSDT');
    const { user, dialog } = await openEntry();
    await within(dialog).findByText('168.40');

    await user.click(within(dialog).getByRole('button', { name: 'Войти' }));

    expect(
      await within(dialog).findByText('Уже открыто столько сделок, сколько разрешено'),
    ).toBeInTheDocument();
    expect(history.location.pathname).toBe('/market/linear/SOLUSDT');
  });

  it('closes the window without entering', async () => {
    const requests = fakeApi(routes());
    renderApp('/market/linear/SOLUSDT');
    const { user, dialog } = await openEntry();

    await user.click(within(dialog).getByRole('button', { name: 'Не входить' }));

    expect(screen.queryByRole('dialog', { name: 'Вход по идее' })).toBeNull();
    expect(
      requests.some(
        (request) => request.url.endsWith('/api/idea-trades') && request.method === 'POST',
      ),
    ).toBe(false);
  });

  it('says so when the settings do not load', async () => {
    fakeApi(routes({ 'GET /api/idea-trading/settings': () => problem(500, 'down') }));
    renderApp('/market/linear/SOLUSDT');

    const { dialog } = await openEntry();

    expect(await within(dialog).findByText('Не удалось загрузить настройки.')).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Войти' })).toBeNull();
  });

  it('says so when the entry cannot be calculated, and names a risk the API refuses', async () => {
    fakeApi(
      routes({
        'GET /api/ideas/idea-pullback/entry': () =>
          problem(400, 'invalid', { errors: { risk: ['The risk must be above zero.'] } }),
      }),
    );
    renderApp('/market/linear/SOLUSDT');

    const { dialog } = await openEntry();

    expect(await within(dialog).findByText('The risk must be above zero.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Войти' })).toBeDisabled();
  });

  it('shows the message of an API that refuses the entry with no reason', async () => {
    fakeApi(
      routes({
        'POST /api/idea-trades': () => problem(503, 'Bybit is unavailable.'),
      }),
    );
    renderApp('/market/linear/SOLUSDT');
    const { user, dialog } = await openEntry();
    await within(dialog).findByText('168.40');

    await user.click(within(dialog).getByRole('button', { name: 'Войти' }));

    expect(await within(dialog).findByText('Bybit is unavailable.')).toBeInTheDocument();
  });
});
