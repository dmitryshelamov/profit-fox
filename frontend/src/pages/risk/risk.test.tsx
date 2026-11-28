import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { Account } from '@/api/accounts';
import { bodiesOf, fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';
import { emptySettings, riskSettings, riskState } from '@/test/trades';

const main: Account = {
  id: 'a1',
  name: 'Основной',
  uid: 1,
  apiKey: 'k',
  marginMode: 'ISOLATED_MARGIN',
  createdAt: '2026-11-27T11:20:00Z',
};

function routes(extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/accounts': () => json([main]),
    'GET /api/risk/state': () => json(riskState()),
    'GET /api/risk/settings': () => json(riskSettings()),
    ...extra,
  });
}

const field = (name: string) => screen.findByRole('textbox', { name });

const halted = riskState({
  halt: {
    haltedAt: Date.UTC(2026, 10, 28, 12, 42),
    loss: 52.1,
    limit: 50,
    notClosed: ['DOGEUSDT'],
    liftedAt: null,
  },
});

// Change take-idea-trades, task 9.3: spec trade-risk «Раздел „Риск“», «Настройки риска», «Сроки и попытки».
describe('the section «Риск»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Состояние риска».
  it('shows the loss of the day, the open trades and the sum in trades beside their limits', async () => {
    routes();
    renderApp('/risk');

    const strip = await screen.findByRole('region', { name: 'Состояние риска' });
    expect(strip).toHaveTextContent('−4.20 из −50.00 USDT');
    expect(strip).toHaveTextContent('2 из 5');
    expect(strip).toHaveTextContent('300 из 1 000 USDT');
    expect(strip).toHaveTextContent('работает');
    expect(strip).toHaveTextContent('Bybit: поток · 1 аккаунт');
    expect(within(strip).queryByTestId('closed-result')).toBeNull();
  });

  // Scenario «Пределы не заданы».
  it('asks to set the limits instead of the state and leaves the limit fields empty', async () => {
    routes({
      'GET /api/risk/state': () => json(riskState({ limitsSet: false })),
      'GET /api/risk/settings': () => json(emptySettings()),
    });
    renderApp('/risk');

    expect(
      await screen.findByText(/Пределы не заданы: вход в идеи невозможен/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Состояние риска' })).toBeNull();
    for (const name of [
      'Сумма на сделку',
      'Открытых сделок',
      'Общая сумма в сделках',
      'Потеря на сделку не больше',
      'Убыток за день не больше',
    ]) {
      expect(await field(name)).toHaveValue('');
    }
  });

  // Scenario «Параметры по группам».
  it('puts the parameters in five groups', async () => {
    routes();
    renderApp('/risk');

    await screen.findByRole('form', { name: 'Параметры риска' });
    const group = (name: string) => within(screen.getByRole('region', { name }));
    await waitFor(() =>
      expect(group('Размер сделки').getByLabelText('Аккаунт по умолчанию')).toHaveValue('a1'),
    );
    expect(group('Размер сделки').getByLabelText('Сумма по умолчанию')).toHaveValue('100');
    expect(group('Размер сделки').getByLabelText('Плечо')).toHaveValue('3');
    expect(group('Пределы входа').getByLabelText('Сумма на сделку')).toHaveValue('200');
    expect(group('Пределы входа').getByLabelText('Открытых сделок')).toHaveValue('5');
    expect(group('Пределы входа').getByLabelText('Общая сумма в сделках')).toHaveValue('1000');
    expect(group('Защита сделки').getByLabelText('Потеря на сделку не больше')).toHaveValue('3');
    expect(group('Стоп-кран').getByLabelText('Убыток за день не больше')).toHaveValue('50');
    const times = group('Сроки и попытки');
    expect(times.getByLabelText('Не входить, если до срока идеи меньше')).toBeInTheDocument();
    expect(times.getByLabelText('Ждать приёма входа')).toBeInTheDocument();
    expect(times.getByLabelText('Попыток вернуть стоп')).toBeInTheDocument();
  });

  // Scenario «Значения по умолчанию».
  it('has 1 minute, 30 seconds and 3 attempts in «Сроки и попытки» on a fresh bench', async () => {
    routes({ 'GET /api/risk/settings': () => json(emptySettings()) });
    renderApp('/risk');

    expect(await field('Не входить, если до срока идеи меньше')).toHaveValue('1');
    expect(await field('Ждать приёма входа')).toHaveValue('30');
    expect(await field('Попыток вернуть стоп')).toHaveValue('3');
  });

  // Scenario «Подсказка к пределу потери».
  it('hints under the loss limit how far half the way to liquidation is at the leverage in the field', async () => {
    routes();
    renderApp('/risk');

    expect(
      await screen.findByText('меньше 16.6 % при плече 3× — половина пути до ликвидации'),
    ).toBeInTheDocument();
    await userEvent.clear(await field('Плечо'));
    await userEvent.type(await field('Плечо'), '10');
    expect(
      await screen.findByText('меньше 5 % при плече 10× — половина пути до ликвидации'),
    ).toBeInTheDocument();
  });

  // Scenario «Правка не сохранена».
  it('marks unsaved edits, sends nothing before «Сохранить» and drops the edits by «Сбросить правки»', async () => {
    const requests = routes();
    renderApp('/risk');
    expect(await screen.findByText(/Правок нет\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeDisabled();

    await userEvent.clear(await field('Плечо'));
    await userEvent.type(await field('Плечо'), '5');
    await userEvent.type(await field('Сумма по умолчанию'), '5');

    expect(
      screen.getByText('2 несохранённые правки · вход считает по прежним'),
    ).toBeInTheDocument();
    expect(requests.some((request) => request.method === 'PUT')).toBe(false);
    await userEvent.click(screen.getByRole('button', { name: 'Сбросить правки' }));
    expect(await field('Плечо')).toHaveValue('3');
    expect(screen.getByText(/Правок нет\./)).toBeInTheDocument();
  });

  it('saves all the parameters with one request and takes the saved ones as the base', async () => {
    const requests = routes({
      'PUT /api/risk/settings': async (request) =>
        json({
          ...riskSettings(),
          ...(await request.json()),
          savedAt: Date.UTC(2026, 10, 28, 12, 0),
        }),
    });
    renderApp('/risk');

    await userEvent.clear(await field('Плечо'));
    await userEvent.type(await field('Плечо'), '5');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'PUT', '/api/risk/settings')).toEqual([
        expect.objectContaining({
          defaultAccountId: 'a1',
          defaultAmount: 100,
          leverage: 5,
          maxAmount: 200,
          maxOpenTrades: 5,
          maxTotalAmount: 1000,
          maxLossPercent: 3,
          maxDayLoss: 50,
          expiryMarginMinutes: 1,
          entryWaitSeconds: 30,
          stopAttempts: 3,
        }),
      ]),
    );
    expect(await screen.findByText(/Параметры сохранены 28.11 15:00/)).toBeInTheDocument();
    expect(await field('Плечо')).toHaveValue('5');
  });

  // Scenarios «Отказ под полем» and «Ожидание вне границ».
  it('tells a refusal under its field and keeps the other edits', async () => {
    routes({
      'PUT /api/risk/settings': () =>
        json(
          {
            status: 422,
            errors: [
              {
                code: 'OutOfRange',
                field: 'maxLossPercent',
                reason: 'При плече 10× предел потери должен быть меньше 5 %',
              },
              { code: 'OutOfRange', field: 'entryWaitSeconds', reason: 'от 10 до 120 секунд' },
            ],
          },
          422,
        ),
    });
    renderApp('/risk');

    await userEvent.clear(await field('Плечо'));
    await userEvent.type(await field('Плечо'), '10');
    await userEvent.clear(await field('Потеря на сделку не больше'));
    await userEvent.type(await field('Потеря на сделку не больше'), '6');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    const loss = await field('Потеря на сделку не больше');
    await waitFor(() =>
      expect(loss).toHaveAccessibleDescription(/предел потери должен быть меньше 5 %/),
    );
    expect(await field('Ждать приёма входа')).toHaveAccessibleDescription('от 10 до 120 секунд');
    expect(await field('Плечо')).toHaveValue('10');
    expect(loss).toHaveValue('6');
    expect(screen.getByText(/2 несохранённые правки/)).toBeInTheDocument();
  });

  // Review R2: a fraction in a field of whole numbers and a number that is not finite are told at their fields, and
  // nothing is sent.
  it('tells a fraction in a whole field and an infinite number at their fields and sends nothing', async () => {
    const requests = routes();
    renderApp('/risk');

    await userEvent.clear(await field('Плечо'));
    await userEvent.type(await field('Плечо'), '2.5');
    await userEvent.clear(await field('Сумма по умолчанию'));
    await userEvent.type(await field('Сумма по умолчанию'), '1e400');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await field('Плечо')).toHaveAccessibleDescription('Введите целое число');
    expect(await field('Сумма по умолчанию')).toHaveAccessibleDescription(/Введите число/);
    expect(requests.filter((request) => request.method === 'PUT')).toHaveLength(0);
    expect(await field('Плечо')).toHaveValue('2.5');
  });

  // Review R2: a refusal whose errors are not a list is one general reason; the page and the draft stay.
  it('shows a refusal of another form as one reason and keeps the draft', async () => {
    routes({
      'PUT /api/risk/settings': () =>
        json(
          {
            title: 'One or more validation errors occurred.',
            status: 400,
            errors: { leverage: ['The JSON value could not be converted.'] },
          },
          400,
        ),
    });
    renderApp('/risk');

    await userEvent.clear(await field('Плечо'));
    await userEvent.type(await field('Плечо'), '4');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByText(/One or more validation errors occurred/)).toBeInTheDocument();
    expect(await field('Плечо')).toHaveValue('4');
  });

  // Scenario «Баннер стоп-крана».
  it('shows the halt with its moment, loss, limit and the pairs not closed, and lifts it', async () => {
    let state = halted;
    const requests = routes({
      'GET /api/risk/state': () => json(state),
      'POST /api/risk/halt/lift': () => {
        state = riskState();
        return new Response(null, { status: 204 });
      },
    });
    renderApp('/risk');

    const banner = await screen.findByRole('region', { name: 'Баннер стоп-крана' });
    expect(banner).toHaveTextContent('28.11 15:42');
    expect(banner).toHaveTextContent('52.10');
    expect(banner).toHaveTextContent('50.00');
    expect(banner).toHaveTextContent('DOGEUSDT');
    await userEvent.click(within(banner).getByRole('button', { name: 'Снять запрет' }));

    await waitFor(() =>
      expect(screen.queryByRole('region', { name: 'Баннер стоп-крана' })).toBeNull(),
    );
    expect(requests.some((request) => request.url.endsWith('/api/risk/halt/lift'))).toBe(true);
  });

  it('shows why the ban cannot be lifted', async () => {
    routes({
      'GET /api/risk/state': () => json(halted),
      'POST /api/risk/halt/lift': () =>
        json({ status: 409, detail: 'сделка DOGEUSDT ещё закрывается' }, 409),
    });
    renderApp('/risk');

    await userEvent.click(await screen.findByRole('button', { name: 'Снять запрет' }));

    expect(await screen.findByText('сделка DOGEUSDT ещё закрывается')).toHaveAttribute(
      'role',
      'alert',
    );
    expect(screen.getByRole('region', { name: 'Баннер стоп-крана' })).toBeInTheDocument();
  });
});
