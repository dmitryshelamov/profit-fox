import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { autoItemOf, autoOf, exchangeAccount, guardsOf, tradingSettings } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const noContent = () => new Response(null, { status: 204 });
const saved = autoOf({ id: 'auto-1', name: 'Импульс лонг' });

function routes(extra: Record<string, (request: Request) => Response | Promise<Response>> = {}) {
  return {
    'GET /api/idea-trading/settings': () =>
      json({ ...tradingSettings, amount: '300', maxAmount: '500' }),
    'GET /api/exchange-accounts': () => json([exchangeAccount]),
    'GET /api/idea-autos': () => json([autoItemOf(saved)]),
    'GET /api/idea-autos/auto-1': () => json(saved),
    'GET /api/idea-trading/guards': () => json(guardsOf()),
    ...extra,
  };
}

const chip = (group: string, name: string) =>
  within(screen.getByRole('group', { name: group })).getByRole('button', { name });

describe('form of an auto', () => {
  // Requirement "Форма автомата", scenario "Новый автомат".
  it('opens a new auto with the account, the amount and the lowest R:R of the settings and no conditions', async () => {
    fakeApi(routes());
    renderApp('/idea-trades/autos/new');

    expect(await screen.findByRole('heading', { name: 'Новый автомат' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText('Аккаунт')).toHaveValue(exchangeAccount.id));
    expect(screen.getByLabelText('Сумма на сделку, USDT')).toHaveValue('300');
    expect(screen.getByText('не больше 500 USDT')).toBeInTheDocument();
    expect(screen.getByLabelText('Минимальный R:R')).toHaveValue('1.5');
    expect(screen.getByLabelText('Название')).toHaveValue('');
    for (const group of ['Сетапы', 'Типы идей', 'ТФ']) {
      expect(
        within(screen.getByRole('group', { name: group }))
          .getAllByRole('button')
          .filter((button) => button.getAttribute('aria-pressed') === 'true'),
      ).toEqual([]);
    }
    expect(screen.getByRole('radio', { name: 'Обе', checked: true })).toBeInTheDocument();
    expect(
      within(screen.getByRole('radiogroup', { name: 'Значок' })).getByRole('radio', {
        name: 'любой',
      }),
    ).toBeChecked();
    expect(
      within(screen.getByRole('radiogroup', { name: 'Режим' })).getByRole('radio', {
        name: 'Выкл',
      }),
    ).toBeChecked();
    expect(screen.getByText(/родившиеся после «Сохранить»/)).toBeInTheDocument();
  });

  // Requirement "Стоп-вход в форме".
  it('shows the stop entry disabled with its caption, and a click does not tick it', async () => {
    fakeApi(routes());
    renderApp('/idea-trades/autos/new');
    const user = userEvent.setup();
    await screen.findByRole('group', { name: 'Типы идей' });

    const stopEntry = chip('Типы идей', 'Стоп-вход');
    await user.click(stopEntry);

    expect(stopEntry).toHaveAttribute('aria-disabled', 'true');
    expect(stopEntry).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByText('Стоп-вход автомат не берёт никогда')).toBeInTheDocument();
  });

  it('opens a saved auto with its values and its name in the title', async () => {
    fakeApi(routes());
    renderApp('/idea-trades/autos/auto-1');

    expect(
      await screen.findByRole('heading', { name: 'Автомат «Импульс лонг»' }),
    ).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText('Название')).toHaveValue('Импульс лонг'));
    expect(chip('Сетапы', '✓ Импульс')).toHaveAttribute('aria-pressed', 'true');
    expect(chip('Сетапы', 'Падение')).toHaveAttribute('aria-pressed', 'false');
    expect(chip('Типы идей', '✓ Откат')).toBeInTheDocument();
    expect(chip('ТФ', '✓ 15 мин')).toBeInTheDocument();
    expect(chip('ТФ', '✓ 1 ч')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Лонг' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Линейный' })).toBeChecked();
    expect(screen.getByRole('radio', { name: '▲' })).toBeChecked();
    expect(screen.getByLabelText('Минимум кейсов')).toHaveValue('100');
    expect(screen.getByRole('radio', { name: 'С деньгами' })).toBeChecked();
    expect(screen.getByText('не хуже выбранного: только ▲')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Путь' })).toHaveTextContent(
      'Сделки / Автоматы / Импульс лонг',
    );
  });

  // Requirement "Сохранение формы".
  it('saves a new auto with the conditions ticked and goes back to the list', async () => {
    const requests = fakeApi(
      routes({
        'POST /api/idea-autos': () => json({ id: 'auto-9' }, 201),
        'PUT /api/idea-autos/auto-9/mode': noContent,
      }),
    );
    const { history } = renderApp('/idea-trades/autos/new');
    const user = userEvent.setup();
    await screen.findByRole('heading', { name: 'Новый автомат' });
    await waitFor(() => expect(screen.getByLabelText('Аккаунт')).toHaveValue(exchangeAccount.id));

    await user.type(screen.getByLabelText('Название'), 'Мой автомат');
    await user.click(chip('Сетапы', 'Импульс'));
    await user.click(chip('Типы идей', 'Откат'));
    await user.click(chip('ТФ', '15 мин'));
    await user.click(chip('ТФ', '1 ч'));
    await user.click(screen.getByRole('radio', { name: 'Шорт' }));
    await user.click(screen.getByRole('radio', { name: 'Спот' }));
    await user.click(screen.getByRole('radio', { name: '≈' }));
    await user.click(screen.getByRole('radio', { name: 'С деньгами' }));
    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(history.location.pathname).toBe('/idea-trades/autos'));
    const post = requests.find((request) => request.method === 'POST')!;

    expect(await post.text()).toBe(
      `{"name":"Мой автомат","accountId":"${exchangeAccount.id}","amount":300,"setups":["impulse"],"types":["pullback"],"intervals":["15","60"],"side":"short","category":"spot","badge":"flat","minRiskReward":1.5,"minCases":0}`,
    );
    expect(
      requests.some((request) => request.method === 'PUT' && request.url.endsWith('/auto-9/mode')),
    ).toBe(true);
  });

  it('saves a saved auto with a PUT and leaves its mode alone when the mode is as it was', async () => {
    const requests = fakeApi(routes({ 'PUT /api/idea-autos/auto-1': noContent }));
    const { history } = renderApp('/idea-trades/autos/auto-1');
    const user = userEvent.setup();
    await waitFor(() => expect(screen.getByLabelText('Название')).toHaveValue('Импульс лонг'));

    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(history.location.pathname).toBe('/idea-trades/autos'));
    expect(requests.filter((request) => request.method === 'PUT')).toHaveLength(1);
  });

  // Requirement "Сохранение формы", scenario "Ошибка суммы".
  it('shows the error of the amount at its field and does not leave the form', async () => {
    fakeApi(
      routes({
        'PUT /api/idea-autos/auto-1': () =>
          problem(400, 'invalid', {
            errors: { amount: ['The amount must not exceed the limit of one trade.'] },
          }),
      }),
    );
    const { history } = renderApp('/idea-trades/autos/auto-1');
    const user = userEvent.setup();
    const amount = await screen.findByLabelText('Сумма на сделку, USDT');
    await waitFor(() => expect(amount).toHaveValue('300'));

    await user.clear(amount);
    await user.type(amount, '600');
    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(
      await screen.findByText('The amount must not exceed the limit of one trade.'),
    ).toBeInTheDocument();
    expect(amount).toHaveAttribute('aria-invalid', 'true');
    expect(history.location.pathname).toBe('/idea-trades/autos/auto-1');
  });

  it('says so when the auto cannot be saved for no reason of a field', async () => {
    fakeApi(routes({ 'PUT /api/idea-autos/auto-1': () => problem(500, 'The database is down.') }));
    renderApp('/idea-trades/autos/auto-1');
    const user = userEvent.setup();
    await waitFor(() => expect(screen.getByLabelText('Название')).toHaveValue('Импульс лонг'));

    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByText('Не удалось сохранить автомат.')).toBeInTheDocument();
  });

  // Requirement "Сохранение формы", scenario "Что изменено".
  it('names the fields that changed next to «Сохранить»', async () => {
    fakeApi(routes());
    renderApp('/idea-trades/autos/auto-1');
    const user = userEvent.setup();
    const rr = await screen.findByLabelText('Минимальный R:R');
    await waitFor(() => expect(rr).toHaveValue('1.5'));

    expect(screen.queryByText(/^изменено:/)).not.toBeInTheDocument();

    await user.click(chip('ТФ', '4 ч'));
    await user.clear(rr);
    await user.type(rr, '1.8');

    expect(screen.getByText('изменено: ТФ, минимальный R:R')).toBeInTheDocument();
  });

  it('goes back to the list on «Отмена» without saving', async () => {
    const requests = fakeApi(routes());
    const { history } = renderApp('/idea-trades/autos/auto-1');
    const user = userEvent.setup();
    const name = await screen.findByLabelText('Название');
    await waitFor(() => expect(name).toHaveValue('Импульс лонг'));

    await user.type(name, ' 2');
    await user.click(screen.getByRole('button', { name: 'Отмена' }));

    await waitFor(() => expect(history.location.pathname).toBe('/idea-trades/autos'));
    expect(requests.some((request) => request.method === 'PUT' || request.method === 'POST')).toBe(
      false,
    );
  });

  it('says so when the auto does not load', async () => {
    fakeApi(routes({ 'GET /api/idea-autos/auto-1': () => problem(404, 'gone') }));
    renderApp('/idea-trades/autos/auto-1');

    expect(await screen.findByText('Не удалось загрузить автомат.')).toBeInTheDocument();
  });
});
