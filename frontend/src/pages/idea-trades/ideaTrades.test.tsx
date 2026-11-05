import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { exchangeAccount, tradeOf, tradingSettings } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

function routes(trades: () => object[], extra: Record<string, () => Response> = {}) {
  return {
    'GET /api/idea-trades': () => json(trades()),
    'GET /api/idea-trading/settings': () => json(tradingSettings),
    'GET /api/exchange-accounts': () => json([exchangeAccount]),
    ...extra,
  };
}

describe('screen «Сделки»', () => {
  // Requirement "Раздел «Сделки»": the table of trades.
  it('shows the trades in a table with their levels, size, status and result', async () => {
    fakeApi(
      routes(() => [
        tradeOf({}),
        tradeOf({
          id: 'closed',
          symbol: 'WIFUSDT',
          status: 'closed_at_target',
          result: '4.68',
          resultR: '2.1',
          filledPrice: '168.5',
        }),
      ]),
    );
    renderApp('/idea-trades');

    const table = await screen.findByRole('table', { name: 'Сделки по идеям' });
    const rows = within(table).getAllByRole('row');

    expect(within(rows[1]!).getByText('SOLUSDT')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('05.11 10:00')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Цель 2R')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Лонг')).toBeInTheDocument();
    for (const value of ['168.4', '165', '175', '0.47', '0.99']) {
      expect(within(rows[1]!).getByText(value)).toBeInTheDocument();
    }
    expect(within(rows[1]!).getByText('Ждёт входа')).toBeInTheDocument();
    expect(within(rows[2]!).getByText('Закрыта по цели')).toBeInTheDocument();
    expect(within(rows[2]!).getByText('+4.68 USDT · +2.10 R')).toBeInTheDocument();
  });

  it('says so when there are no trades', async () => {
    fakeApi(routes(() => []));
    renderApp('/idea-trades');

    expect(await screen.findByText('Сделок по идеям пока нет.')).toBeInTheDocument();
  });

  it('offers to cancel a trade that waits and to close one that is open, and nothing for the others', async () => {
    fakeApi(
      routes(() => [
        tradeOf({ id: 'a' }),
        tradeOf({ id: 'b', status: 'open' }),
        tradeOf({ id: 'c', status: 'cancelled' }),
      ]),
    );
    renderApp('/idea-trades');

    const rows = within(await screen.findByRole('table', { name: 'Сделки по идеям' })).getAllByRole(
      'row',
    );

    expect(within(rows[1]!).getByRole('button', { name: 'Отменить' })).toBeInTheDocument();
    expect(within(rows[1]!).queryByRole('button', { name: 'Закрыть по рынку' })).toBeNull();
    expect(within(rows[2]!).getByRole('button', { name: 'Закрыть по рынку' })).toBeInTheDocument();
    expect(within(rows[3]!).queryByRole('button')).toBeNull();
  });

  it('shows the reason of a trade in «Ошибка»', async () => {
    fakeApi(
      routes(() => [
        tradeOf({ status: 'failed', reason: 'Exchange position 0 does not match the positions.' }),
      ]),
    );
    renderApp('/idea-trades');

    expect(
      await screen.findByText('Exchange position 0 does not match the positions.'),
    ).toBeInTheDocument();
  });

  // Scenario "Закрытие с подтверждением".
  it('closes an open trade by the market after the owner confirms, and shows the result', async () => {
    let closed = false;
    const requests = fakeApi(
      routes(
        () => [
          closed
            ? tradeOf({ status: 'closed_manually', result: '0.31', resultR: '0.3' })
            : tradeOf({ status: 'open' }),
        ],
        {
          'POST /api/idea-trades/aaaaaaaa-0000-4000-8000-000000000001/close': () => {
            closed = true;

            return new Response(null, { status: 204 });
          },
        },
      ),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Закрыть по рынку' }));
    const dialog = await screen.findByRole('dialog');
    expect(requests.some((request) => request.url.endsWith('/close'))).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Закрыть по рынку' }));

    expect(await screen.findByText('Закрыта вручную')).toBeInTheDocument();
    expect(screen.getByText('+0.31 USDT · +0.30 R')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('cancels the entry of a trade after the owner confirms', async () => {
    const requests = fakeApi(
      routes(() => [tradeOf({})], {
        'POST /api/idea-trades/aaaaaaaa-0000-4000-8000-000000000001/cancel': () =>
          new Response(null, { status: 204 }),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Отменить' }));
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Отменить сделку' }),
    );

    await waitFor(() =>
      expect(requests.some((request) => request.url.endsWith('/cancel'))).toBe(true),
    );
  });

  it('shows the refusal of the API in the question and keeps it open', async () => {
    fakeApi(
      routes(() => [tradeOf({ status: 'open' })], {
        'POST /api/idea-trades/aaaaaaaa-0000-4000-8000-000000000001/close': () =>
          problem(409, 'Only an open trade is closed.'),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Закрыть по рынку' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Закрыть по рынку' }));

    expect(await within(dialog).findByText('Only an open trade is closed.')).toBeInTheDocument();
  });

  it('keeps the trade when the owner does not confirm', async () => {
    const requests = fakeApi(routes(() => [tradeOf({ status: 'open' })]));
    renderApp('/idea-trades');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Закрыть по рынку' }));
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Не закрывать' }),
    );

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });
});

describe('settings of trading by ideas', () => {
  it('shows the settings and the accounts to choose from', async () => {
    fakeApi(routes(() => []));
    renderApp('/idea-trades');

    const panel = await screen.findByRole('region', { name: 'Настройки' });

    await waitFor(() =>
      expect(within(panel).getByLabelText('Риск на сделку, USDT')).toHaveValue('1'),
    );
    expect(within(panel).getByLabelText('Не больше суммы на сделку, USDT')).toHaveValue('200');
    expect(within(panel).getByLabelText('Не больше открытых сделок')).toHaveValue('5');
    expect(within(panel).getByLabelText('Не больше общего риска, USDT')).toHaveValue('5');
    expect(within(panel).getByLabelText('Минимальный R:R после комиссий')).toHaveValue('1.5');
    expect(within(panel).getByLabelText('Аккаунт по умолчанию')).toHaveValue(exchangeAccount.id);
  });

  // Scenario "Сохранение лимита".
  it('saves the limit of open trades as typed', async () => {
    const requests = fakeApi(
      routes(() => [], {
        'PUT /api/idea-trading/settings': () => new Response(null, { status: 204 }),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();
    const panel = await screen.findByRole('region', { name: 'Настройки' });
    const limit = await within(panel).findByLabelText('Не больше открытых сделок');
    await waitFor(() => expect(limit).toHaveValue('5'));

    await user.clear(limit);
    await user.type(limit, '3');
    await user.click(within(panel).getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    expect(await requests.find((request) => request.method === 'PUT')!.text()).toBe(
      `{"accountId":"${exchangeAccount.id}","risk":1,"maxAmount":200,"maxOpenTrades":3,"maxTotalRisk":5,"minRiskReward":1.5}`,
    );
    expect(await screen.findByText('Настройки сохранены')).toBeInTheDocument();
  });

  it('says so when the settings cannot be saved', async () => {
    fakeApi(
      routes(() => [], {
        'PUT /api/idea-trading/settings': () => problem(500, 'The database is down.'),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();
    const panel = await screen.findByRole('region', { name: 'Настройки' });
    await within(panel).findByLabelText('Риск на сделку, USDT');

    await user.click(within(panel).getByRole('button', { name: 'Сохранить' }));

    expect(await within(panel).findByText('Не удалось сохранить настройки.')).toBeInTheDocument();
  });

  it('says so when the settings do not load', async () => {
    fakeApi(routes(() => [], { 'GET /api/idea-trading/settings': () => problem(404, 'down') }));
    renderApp('/idea-trades');

    expect(await screen.findByText('Не удалось загрузить настройки.')).toBeInTheDocument();
  });

  // Scenario "Риск ноль": the error stands at its field.
  it('shows an error of validation at its field', async () => {
    fakeApi(
      routes(() => [], {
        'PUT /api/idea-trading/settings': () =>
          problem(400, 'invalid', { errors: { risk: ['The risk must be above zero.'] } }),
      }),
    );
    renderApp('/idea-trades');
    const user = userEvent.setup();
    const panel = await screen.findByRole('region', { name: 'Настройки' });
    const risk = await within(panel).findByLabelText('Риск на сделку, USDT');
    await waitFor(() => expect(risk).toHaveValue('1'));

    await user.clear(risk);
    await user.type(risk, '0');
    await user.click(within(panel).getByRole('button', { name: 'Сохранить' }));

    expect(await within(panel).findByText('The risk must be above zero.')).toBeInTheDocument();
    expect(risk).toHaveAttribute('aria-invalid', 'true');
  });
});
