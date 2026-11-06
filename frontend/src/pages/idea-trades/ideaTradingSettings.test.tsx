import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { exchangeAccount, journalOf, tradingSettings } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

function routes(journal: () => object, extra: Record<string, () => Response> = {}) {
  return {
    'GET /api/idea-trades': () => json(journal()),
    'GET /api/idea-trading/settings': () => json(tradingSettings),
    'GET /api/exchange-accounts': () => json([exchangeAccount]),
    ...extra,
  };
}

describe('settings of trading by ideas', () => {
  it('shows the settings and the accounts to choose from', async () => {
    fakeApi(routes(() => journalOf()));
    renderApp('/idea-trades/settings');

    const panel = await screen.findByRole('region', { name: 'Настройки' });

    await waitFor(() =>
      expect(within(panel).getByLabelText('Риск на сделку, USDT')).toHaveValue('1'),
    );
    expect(within(panel).getByLabelText('Не больше суммы на сделку, USDT')).toHaveValue('200');
    expect(within(panel).getByLabelText('Не больше открытых сделок')).toHaveValue('5');
    expect(within(panel).getByLabelText('Не больше общего риска, USDT')).toHaveValue('5');
    expect(within(panel).getByLabelText('Минимальный R:R после комиссий')).toHaveValue('1.5');
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
    renderApp('/idea-trades/settings');
    const user = userEvent.setup();
    const panel = await screen.findByRole('region', { name: 'Настройки' });
    await within(panel).findByLabelText('Риск на сделку, USDT');

    await user.click(within(panel).getByRole('button', { name: 'Сохранить' }));

    expect(await within(panel).findByText('Не удалось сохранить настройки.')).toBeInTheDocument();
  });

  it('says so when the settings do not load', async () => {
    fakeApi(routes(() => [], { 'GET /api/idea-trading/settings': () => problem(404, 'down') }));
    renderApp('/idea-trades/settings');

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
    renderApp('/idea-trades/settings');
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
