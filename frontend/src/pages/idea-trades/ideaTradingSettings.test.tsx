import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { exchangeAccount, guardsOf, journalOf, tradingSettings } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

function routes(journal: () => object, extra: Record<string, () => Response> = {}) {
  return {
    'GET /api/idea-trades': () => json(journal()),
    'GET /api/idea-trading/settings': () => json(tradingSettings),
    'GET /api/exchange-accounts': () => json([exchangeAccount]),
    'GET /api/idea-trading/guards': () => json(guardsOf()),
    ...extra,
  };
}

describe('settings of trading by ideas', () => {
  // Scenario "Подписи настроек".
  it('shows the settings and the accounts to choose from', async () => {
    fakeApi(routes(() => journalOf()));
    renderApp('/idea-trades/settings');

    const panel = await screen.findByRole('region', { name: 'Настройки' });

    await waitFor(() =>
      expect(within(panel).getByLabelText('Сумма сделки по умолчанию, USDT')).toHaveValue('20'),
    );
    expect(within(panel).getByLabelText('Максимум на одну сделку, USDT')).toHaveValue('200');
    expect(within(panel).getByLabelText('Не больше открытых сделок')).toHaveValue('5');
    expect(
      within(panel).getByLabelText('Максимальный убыток, если сработают все стопы, USDT'),
    ).toHaveValue('5');
    expect(within(panel).getByLabelText('Прибыль к убытку не меньше')).toHaveValue('1.5');
    expect(
      within(panel).getByText('подставляется в окно входа и в новый автомат'),
    ).toBeInTheDocument();
    expect(
      within(panel).getByText(
        'больше вписать нельзя — защита от опечатки; и для ручных входов, и для автоматов',
      ),
    ).toBeInTheDocument();
    expect(
      within(panel).getByText(
        '1.5 — цель должна дать в полтора раза больше, чем заберёт стоп; для ручных входов',
      ),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(within(panel).getByLabelText('Аккаунт по умолчанию')).toHaveValue(exchangeAccount.id),
    );
  });

  // Scenario "Сохранение лимита".
  it('saves the limit of open trades as typed', async () => {
    const requests = fakeApi(
      routes(() => [], {
        'PUT /api/idea-trading/settings': () => new Response(null, { status: 204 }),
      }),
    );
    renderApp('/idea-trades/settings');
    const user = userEvent.setup();
    const panel = await screen.findByRole('region', { name: 'Настройки' });
    const limit = await within(panel).findByLabelText('Не больше открытых сделок');
    await waitFor(() => expect(limit).toHaveValue('5'));

    await user.clear(limit);
    await user.type(limit, '3');
    await user.click(within(panel).getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    expect(await requests.find((request) => request.method === 'PUT')!.text()).toBe(
      `{"accountId":"${exchangeAccount.id}","amount":20,"maxAmount":200,"maxOpenTrades":3,"maxTotalRisk":5,"minRiskReward":1.5,"dailyLossLimit":null,"maxSameDirection":null,"autosBudget":null}`,
    );
    expect(await screen.findByText('Настройки сохранены')).toBeInTheDocument();
  });

  it('says so when the settings cannot be saved', async () => {
    fakeApi(
      routes(() => [], {
        'PUT /api/idea-trading/settings': () => problem(500, 'The database is down.'),
      }),
    );
    renderApp('/idea-trades/settings');
    const user = userEvent.setup();
    const panel = await screen.findByRole('region', { name: 'Настройки' });
    await within(panel).findByLabelText('Сумма сделки по умолчанию, USDT');

    await user.click(within(panel).getByRole('button', { name: 'Сохранить' }));

    expect(await within(panel).findByText('Не удалось сохранить настройки.')).toBeInTheDocument();
  });

  it('says so when the settings do not load', async () => {
    fakeApi(routes(() => [], { 'GET /api/idea-trading/settings': () => problem(404, 'down') }));
    renderApp('/idea-trades/settings');

    expect(await screen.findByText('Не удалось загрузить настройки.')).toBeInTheDocument();
  });

  // Scenario "Сумма ноль": the error stands at its field.
  it('shows an error of validation at its field', async () => {
    fakeApi(
      routes(() => [], {
        'PUT /api/idea-trading/settings': () =>
          problem(400, 'invalid', { errors: { amount: ['The amount must be above zero.'] } }),
      }),
    );
    renderApp('/idea-trades/settings');
    const user = userEvent.setup();
    const panel = await screen.findByRole('region', { name: 'Настройки' });
    const amount = await within(panel).findByLabelText('Сумма сделки по умолчанию, USDT');
    await waitFor(() => expect(amount).toHaveValue('20'));

    await user.clear(amount);
    await user.type(amount, '0');
    await user.click(within(panel).getByRole('button', { name: 'Сохранить' }));

    expect(await within(panel).findByText('The amount must be above zero.')).toBeInTheDocument();
    expect(amount).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('guards of the autos on the settings screen', () => {
  const guarded = {
    'GET /api/idea-trading/settings': () =>
      json({ ...tradingSettings, dailyLossLimit: '30', maxSameDirection: 5 }),
  };

  // Requirement "Экран настроек торговли идеями".
  it('has the block «Защиты автоматов» that says they stop only the autos with money', async () => {
    fakeApi(routes(() => [], guarded));
    renderApp('/idea-trades/settings');

    const block = await screen.findByRole('region', { name: 'Защиты автоматов' });

    expect(block).toHaveTextContent('останавливают только входы автоматов с деньгами');
    await waitFor(() =>
      expect(within(block).getByLabelText('Дневной стоп по убытку, USDT')).toHaveValue('30'),
    );
    expect(within(block).getByLabelText('Не больше сделок в одну сторону')).toHaveValue('5');
  });

  // Scenario "Состояние дневного стопа".
  it('shows the state of every guard under its field, with a bar for the day', async () => {
    fakeApi(routes(() => [], guarded));
    renderApp('/idea-trades/settings');

    const block = await screen.findByRole('region', { name: 'Защиты автоматов' });

    expect(await within(block).findByText('Сегодня: -19.05 из 30 USDT')).toBeInTheDocument();
    expect(
      within(block).getByText('открытых реальных: в лонг 2 · в шорт 1 из 5'),
    ).toBeInTheDocument();
    expect(await screen.findByText('сейчас открыто 3 из 8')).toBeInTheDocument();
    expect(screen.getByText('сейчас 10.22 из 40 USDT')).toBeInTheDocument();
    expect(block.querySelector('[style*="width"]')).toHaveStyle({ width: '63.5%' });
  });

  // Scenario "Состояние бюджета автоматов".
  it('shows what the trades of the autos take of their budget under its field and saves it', async () => {
    const requests = fakeApi(
      routes(() => [], {
        'GET /api/idea-trading/settings': () => json({ ...tradingSettings, autosBudget: '200' }),
        'PUT /api/idea-trading/settings': () => new Response(null, { status: 204 }),
      }),
    );
    renderApp('/idea-trades/settings');
    const user = userEvent.setup();
    const block = await screen.findByRole('region', { name: 'Защиты автоматов' });
    const budget = within(block).getByLabelText('Бюджет всех автоматов, USDT');
    await waitFor(() => expect(budget).toHaveValue('200'));

    expect(await within(block).findByText('сейчас 180.00 из 200 USDT')).toBeInTheDocument();
    await user.clear(budget);
    await user.type(budget, '250.5');
    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    expect(await requests.find((request) => request.method === 'PUT')!.text()).toContain(
      '"autosBudget":250.5}',
    );
  });

  // Scenario "Бюджет автоматов выключен".
  it('says the budget of the autos is off while its field is empty', async () => {
    fakeApi(
      routes(() => [], {
        'GET /api/idea-trading/guards': () => json(guardsOf({ autosBudget: null })),
      }),
    );
    renderApp('/idea-trades/settings');

    const block = await screen.findByRole('region', { name: 'Защиты автоматов' });

    expect(
      await within(block).findByText('сейчас 180.00 USDT · защита выключена'),
    ).toBeInTheDocument();
    expect(within(block).getByLabelText('Бюджет всех автоматов, USDT')).toHaveValue('');
  });

  it('shows that a guard that is off is off, with the field empty', async () => {
    fakeApi(
      routes(() => [], {
        'GET /api/idea-trading/guards': () =>
          json(guardsOf({ dailyLossLimit: null, maxSameDirection: null })),
      }),
    );
    renderApp('/idea-trades/settings');

    const block = await screen.findByRole('region', { name: 'Защиты автоматов' });

    expect(
      await within(block).findByText('Сегодня: -19.05 USDT · защита выключена'),
    ).toBeInTheDocument();
    expect(within(block).getByLabelText('Дневной стоп по убытку, USDT')).toHaveValue('');
    expect(block.querySelector('[style*="width"]')).toBeNull();
  });

  // An empty field saves as a guard that is off.
  it('saves an empty field as a guard that is off, and a number as typed', async () => {
    const requests = fakeApi(
      routes(() => [], {
        ...guarded,
        'PUT /api/idea-trading/settings': () => new Response(null, { status: 204 }),
      }),
    );
    renderApp('/idea-trades/settings');
    const user = userEvent.setup();
    const block = await screen.findByRole('region', { name: 'Защиты автоматов' });
    const daily = within(block).getByLabelText('Дневной стоп по убытку, USDT');
    const sides = within(block).getByLabelText('Не больше сделок в одну сторону');
    await waitFor(() => expect(daily).toHaveValue('30'));

    await user.clear(daily);
    await user.clear(sides);
    await user.type(sides, '4');
    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    expect(await requests.find((request) => request.method === 'PUT')!.text()).toContain(
      '"dailyLossLimit":null,"maxSameDirection":4,"autosBudget":null}',
    );
  });

  it('shows an error of a guard at its field', async () => {
    fakeApi(
      routes(() => [], {
        'PUT /api/idea-trading/settings': () =>
          problem(400, 'invalid', {
            errors: { dailyLossLimit: ['The limit must be above zero.'] },
          }),
      }),
    );
    renderApp('/idea-trades/settings');
    const user = userEvent.setup();
    const block = await screen.findByRole('region', { name: 'Защиты автоматов' });
    await within(block).findByLabelText('Дневной стоп по убытку, USDT');

    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await within(block).findByText('The limit must be above zero.')).toBeInTheDocument();
    expect(within(block).getByLabelText('Дневной стоп по убытку, USDT')).toHaveAttribute(
      'aria-invalid',
      'true',
    );
  });

  // Requirement "Баннер дневного стопа".
  it('shows the banner and when the autos enter again while the daily stop holds', async () => {
    fakeApi(
      routes(() => [], {
        ...guarded,
        'GET /api/idea-trading/guards': () =>
          json(
            guardsOf({
              daySum: '-30.15',
              triggeredAt: '2026-11-07T15:12:00Z',
              resumesAt: '2026-11-08T00:00:00Z',
            }),
          ),
      }),
    );
    renderApp('/idea-trades/settings');

    const banner = await screen.findByRole('status', { name: 'Дневной стоп' });

    expect(banner).toHaveTextContent('Сегодня -30.15 из 30 USDT · сработал в 15:12 UTC');
    expect(await screen.findByText('автоматы снова входят с 08.11 00:00 UTC')).toBeInTheDocument();
  });

  it('shows no banner while the stop has not held', async () => {
    fakeApi(routes(() => [], guarded));
    renderApp('/idea-trades/settings');
    await screen.findByRole('region', { name: 'Защиты автоматов' });

    expect(screen.queryByRole('status', { name: 'Дневной стоп' })).not.toBeInTheDocument();
  });
});
