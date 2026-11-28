import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { Account } from '@/api/accounts';
import { bodiesOf, fakeApi, type FakeRoute, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

const main: Account = {
  id: 'a1',
  name: 'Основной',
  uid: 104857321,
  apiKey: 'k3VbQ9xT2mLpA7dRw1',
  marginMode: 'ISOLATED_MARGIN',
  createdAt: '2026-11-27T11:20:00Z',
};

const ideas: Account = {
  id: 'a2',
  name: 'Суб-счёт идей',
  uid: 104857998,
  apiKey: 'Zt81HcNq0sYeU4oPj6',
  marginMode: null,
  createdAt: '2026-11-28T03:05:00Z',
};

function routes(accounts: readonly Account[], extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    ...baseRoutes(),
    'GET /api/accounts': () => json(accounts),
    ...extra,
  });
}

const row = (name: string) => screen.findByRole('row', { name: `Аккаунт ${name}` });

async function openDialog() {
  await userEvent.click(await screen.findByRole('button', { name: '+ Добавить аккаунт' }));
  return within(await screen.findByRole('dialog', { name: 'Новый аккаунт' }));
}

async function fill(dialog: ReturnType<typeof within>, name: string, key: string, secret: string) {
  await userEvent.type(dialog.getByRole('textbox', { name: 'Имя' }), name);
  await userEvent.type(dialog.getByRole('textbox', { name: 'Ключ API' }), key);
  await userEvent.type(dialog.getByLabelText('Секрет'), secret);
}

// Change add-exchange-accounts, tasks 6.2–6.4: spec exchange-accounts «Раздел «Аккаунты»».
describe('the section «Аккаунты»', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Пустой раздел».
  it('says an account is needed for the trades of ideas and offers to add one when there is none', async () => {
    routes([]);
    renderApp('/accounts');

    const empty = await screen.findByRole('region', { name: 'Аккаунтов нет' });
    expect(empty).toHaveTextContent('Аккаунт Bybit нужен для сделок по идеям');
    await userEvent.click(within(empty).getByRole('button', { name: '+ Добавить аккаунт' }));
    expect(await screen.findByRole('dialog', { name: 'Новый аккаунт' })).toBeInTheDocument();
  });

  it('lists the accounts with their UID, key, the permissions of the key and when they were added', async () => {
    routes([main, ideas]);
    renderApp('/accounts');

    const first = within(await row('Основной'));
    expect(first.getByText('104857321')).toBeInTheDocument();
    expect(first.getByText('k3VbQ9xT2mLpA7dRw1')).toBeInTheDocument();
    expect(first.getByText('торговля · бессрочные')).toBeInTheDocument();
    // Tests run in UTC+3.
    expect(first.getByText('27.11 14:20')).toBeInTheDocument();
    expect(await row('Суб-счёт идей')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Добавлен, UTC+3' })).toBeInTheDocument();
  });

  it('hides the secret as it is typed and adds the account the server took, closing the dialog', async () => {
    const saved: Account[] = [];
    const requests = fakeApi({
      ...baseRoutes(),
      'GET /api/accounts': () => json(saved),
      'POST /api/accounts': () => {
        saved.push(main);
        return json(main, 201);
      },
    });
    renderApp('/accounts');
    await userEvent.click(await screen.findByRole('button', { name: '+ Добавить аккаунт' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Новый аккаунт' }));

    expect(dialog.getByLabelText('Секрет')).toHaveAttribute('type', 'password');
    expect(dialog.getByLabelText('Секрет')).toHaveAttribute('autocomplete', 'off');
    await fill(dialog, 'Основной', 'k3VbQ9xT2mLpA7dRw1', 's3cr3t-value');
    await userEvent.click(dialog.getByRole('button', { name: 'Проверить и сохранить' }));

    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Новый аккаунт' })).not.toBeInTheDocument(),
    );
    expect(await row('Основной')).toBeInTheDocument();
    expect(await bodiesOf(requests, 'POST', '/api/accounts')).toEqual([
      { name: 'Основной', apiKey: 'k3VbQ9xT2mLpA7dRw1', secret: 's3cr3t-value' },
    ]);
  });

  // Scenario «Отказ в диалоге».
  it('keeps the dialog open with the reason under the key and the secret emptied when the key may withdraw', async () => {
    routes([main], {
      'POST /api/accounts': () =>
        json(
          {
            errors: [
              { code: 'WithdrawPermission', reason: 'у ключа есть право вывода', field: 'apiKey' },
            ],
          },
          422,
        ),
    });
    renderApp('/accounts');
    const dialog = await openDialog();
    await fill(dialog, 'Второй', 'XvK3pTq9AbCdEf1234', 's3cr3t-value');

    await userEvent.click(dialog.getByRole('button', { name: 'Проверить и сохранить' }));

    const key = dialog.getByRole('textbox', { name: 'Ключ API' });
    await waitFor(() => expect(key).toHaveAttribute('aria-invalid', 'true'));
    expect(key).toHaveAccessibleDescription('у ключа есть право вывода');
    expect(dialog.getByLabelText('Секрет')).toHaveValue('');
    expect(dialog.getByRole('textbox', { name: 'Имя' })).toHaveValue('Второй');
    expect(screen.getByRole('dialog', { name: 'Новый аккаунт' })).toBeInTheDocument();
  });

  it('names Bybit when Bybit does not take the key, and tells an exchange that did not answer in the dialog', async () => {
    let answer = 0;
    routes([], {
      'POST /api/accounts': () =>
        ++answer === 1
          ? json(
              { errors: [{ code: 'KeyRejected', reason: 'API key is invalid.', field: 'apiKey' }] },
              422,
            )
          : json({ status: 503, detail: 'Bybit не отвечает: попробуйте ещё раз позже.' }, 503),
    });
    renderApp('/accounts');
    const dialog = await openDialog();
    await fill(dialog, 'Основной', 'bad-key', 'secret');

    await userEvent.click(dialog.getByRole('button', { name: 'Проверить и сохранить' }));
    expect(
      await dialog.findByText('Bybit не принял ключ: API key is invalid.'),
    ).toBeInTheDocument();

    await userEvent.type(dialog.getByLabelText('Секрет'), 'secret');
    await userEvent.click(dialog.getByRole('button', { name: 'Проверить и сохранить' }));
    expect(
      await dialog.findByText('Bybit не отвечает: попробуйте ещё раз позже.'),
    ).toBeInTheDocument();
    expect(dialog.getByLabelText('Секрет')).toHaveValue('');
  });

  // Scenario «Идёт проверка».
  it('makes the button of saving wait and say «Проверяем ключ» while Bybit checks the key', async () => {
    let answer: (response: Response) => void = () => undefined;
    routes([], {
      'POST /api/accounts': () => new Promise<Response>((resolve) => (answer = resolve)),
    });
    renderApp('/accounts');
    const dialog = await openDialog();
    await fill(dialog, 'Основной', 'k3VbQ9xT2mLpA7dRw1', 's3cr3t-value');

    await userEvent.click(dialog.getByRole('button', { name: 'Проверить и сохранить' }));

    const waiting = await dialog.findByRole('button', { name: 'Проверяем ключ' });
    expect(waiting).toBeDisabled();
    answer(json(main, 201));
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Новый аккаунт' })).not.toBeInTheDocument(),
    );
  });

  // Scenario «Подтверждение удаления».
  it('asks before a removal, naming the account, and removes only after the answer', async () => {
    const requests = routes([main, ideas], {
      'DELETE /api/accounts/a1': () => new Response(null, { status: 204 }),
    });
    renderApp('/accounts');
    await row('Основной');

    await userEvent.click(screen.getByRole('button', { name: 'Действия Основной' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить' }));
    const question = within(
      await screen.findByRole('dialog', { name: 'Удалить аккаунт Основной' }),
    );
    expect(question.getByText(/Удалить аккаунт «Основной»\?/)).toBeInTheDocument();
    await userEvent.click(question.getByRole('button', { name: 'Отмена' }));
    expect(requests.filter((request) => request.method === 'DELETE')).toEqual([]);

    await userEvent.click(screen.getByRole('button', { name: 'Действия Основной' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Удалить' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Удалить аккаунт Основной' })).getByRole(
        'button',
        { name: 'Удалить' },
      ),
    );

    await waitFor(() =>
      expect(screen.queryByRole('row', { name: 'Аккаунт Основной' })).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('row', { name: 'Аккаунт Суб-счёт идей' })).toBeInTheDocument();
    expect(requests.filter((request) => request.method === 'DELETE')).toHaveLength(1);
  });

  it('renames an account and tells a taken name under the field', async () => {
    let answer = 0;
    const requests = routes([main, ideas], {
      'PUT /api/accounts/a1': () =>
        ++answer === 1
          ? json(
              {
                errors: [
                  { code: 'TakenName', reason: 'аккаунт «суб-счёт идей» уже есть', field: 'name' },
                ],
              },
              422,
            )
          : json({ ...main, name: 'Запасной' }),
    });
    renderApp('/accounts');
    await row('Основной');
    await userEvent.click(screen.getByRole('button', { name: 'Действия Основной' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Переименовать' }));
    const dialog = within(
      await screen.findByRole('dialog', { name: 'Переименовать аккаунт Основной' }),
    );
    const field = dialog.getByRole('textbox', { name: 'Новое имя' });

    await userEvent.clear(field);
    await userEvent.type(field, 'суб-счёт идей');
    await userEvent.click(dialog.getByRole('button', { name: 'Переименовать' }));
    expect(await dialog.findByText('аккаунт «суб-счёт идей» уже есть')).toBeInTheDocument();

    await userEvent.clear(field);
    await userEvent.type(field, 'Запасной');
    await userEvent.click(dialog.getByRole('button', { name: 'Переименовать' }));

    expect(await row('Запасной')).toBeInTheDocument();
    expect(await bodiesOf(requests, 'PUT', '/api/accounts/a1')).toEqual([
      { name: 'суб-счёт идей' },
      { name: 'Запасной' },
    ]);
  });

  // Scenario «Баланс в строке».
  it('shows the free USDT with a button to read them again, and a refusal of Bybit in red', async () => {
    let free = 0;
    const requests = routes([main, ideas], {
      'GET /api/accounts/a1/balance': () =>
        json({ free: ++free === 1 ? 1250.4 : 1300, marginMode: 'ISOLATED_MARGIN' }),
      'GET /api/accounts/a2/balance': () =>
        json(
          {
            errors: [{ code: 'KeyRejected', reason: 'срок ключа истёк', field: 'apiKey' }],
          },
          422,
        ),
    });
    renderApp('/accounts');
    const first = within(await row('Основной'));
    const second = within(await row('Суб-счёт идей'));

    await userEvent.click(first.getByRole('button', { name: 'Показать баланс Основной' }));
    expect(await first.findByText('1 250.40')).toBeInTheDocument();
    await userEvent.click(first.getByRole('button', { name: 'Обновить баланс Основной' }));
    expect(await first.findByText('1 300.00')).toBeInTheDocument();

    await userEvent.click(second.getByRole('button', { name: 'Показать баланс Суб-счёт идей' }));
    const refusal = await second.findByRole('alert');
    expect(refusal).toHaveTextContent('Bybit отклоняет ключ: срок ключа истёк');
    expect(refusal).toHaveClass('text-err');
    expect(
      requests.filter((request) => new URL(request.url).pathname.endsWith('/balance')),
    ).toHaveLength(3);
  });

  // Change add-exchange-accounts, task 6.6: spec «Режим маржи аккаунта» — «Режим словами», «Неизвестный режим», «Список не
  // ходит к Bybit».
  it('writes the kept margin mode in words between the permissions and the free money, and «—» for another or none', async () => {
    const requests = routes([
      main,
      { ...ideas, marginMode: 'REGULAR_MARGIN' },
      { ...main, id: 'a3', name: 'Портфель', uid: 3, marginMode: 'PORTFOLIO_MARGIN' },
      { ...main, id: 'a4', name: 'Новый режим', uid: 4, marginMode: 'FUTURE_MARGIN' },
      { ...main, id: 'a5', name: 'Без режима', uid: 5, marginMode: null },
    ]);
    renderApp('/accounts');

    await row('Основной');
    const headers = screen.getAllByRole('columnheader').map((header) => header.textContent);
    expect(headers.indexOf('Режим маржи')).toBe(headers.indexOf('Права ключа') + 1);
    expect(headers.indexOf('Свободно, USDT')).toBe(headers.indexOf('Режим маржи') + 1);
    expect(within(await row('Основной')).getByText('изолированная')).toBeInTheDocument();
    expect(within(await row('Суб-счёт идей')).getByText('кросс')).toBeInTheDocument();
    expect(within(await row('Портфель')).getByText('портфельная')).toBeInTheDocument();
    expect(within(await row('Новый режим')).getByText('—')).toBeInTheDocument();
    expect(within(await row('Без режима')).getByText('—')).toBeInTheDocument();
    expect(
      requests.filter((request) => new URL(request.url).pathname.startsWith('/api/accounts/')),
    ).toEqual([]);
  });

  // Scenario «Режим обновлён по „Показать“».
  it('shows the margin mode read with the balance in place of the kept one', async () => {
    routes([main], {
      'GET /api/accounts/a1/balance': () => json({ free: 10, marginMode: 'REGULAR_MARGIN' }),
    });
    renderApp('/accounts');
    const first = within(await row('Основной'));
    expect(first.getByText('изолированная')).toBeInTheDocument();

    await userEvent.click(first.getByRole('button', { name: 'Показать баланс Основной' }));

    expect(await first.findByText('кросс')).toBeInTheDocument();
    expect(first.queryByText('изолированная')).not.toBeInTheDocument();
    expect(first.getByText('10.00')).toBeInTheDocument();
  });
});
