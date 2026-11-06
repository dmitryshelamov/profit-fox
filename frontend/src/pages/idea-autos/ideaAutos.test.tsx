import { createEvent, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { autoItemOf, autoOf, exchangeAccount, guardsOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const noContent = () => new Response(null, { status: 204 });

const impulse = autoOf({ id: 'auto-1', name: 'Импульс лонг', position: 0 });
const drops = autoOf({ id: 'auto-2', name: 'Падения шорт', side: 'short', position: 1 });
const breakouts = autoOf({
  id: 'auto-3',
  name: 'Пробои 5м',
  mode: 'paper',
  setups: ['breakout', 'false_breakout'],
  types: ['levels'],
  intervals: ['5', '15'],
  side: null,
  category: null,
  badge: null,
  minRiskReward: '1.2',
  minCases: 50,
  position: 2,
});

/** The list the test names; the guards and the rest of the API are quiet. */
function routes(
  list: () => object[],
  extra: Record<string, (request: Request) => Response | Promise<Response>> = {},
) {
  return {
    'GET /api/idea-autos': () => json(list()),
    'GET /api/idea-trading/guards': () => json(guardsOf()),
    ...extra,
  };
}

const rowOf = (name: string) =>
  within(screen.getByRole('table', { name: 'Автоматы' }))
    .getByText(name)
    .closest('tr')!;

describe('screen «Автоматы»', () => {
  // Requirement "Экран «Автоматы»", scenario "Учебный автомат в списке".
  it('shows a paper auto with its mark, its paper totals and that there was no money', async () => {
    fakeApi(
      routes(() => [
        autoItemOf(breakouts, { paperClosed: 61, paperMeanR: '0.0713', paperSumR: '4.27' }),
      ]),
    );
    renderApp('/idea-trades/autos');

    await screen.findByRole('table', { name: 'Автоматы' });
    const row = rowOf('Пробои 5м');

    expect(within(row).getByText('учебные сделки')).toBeInTheDocument();
    expect(within(row).getByText('61 · ср. +0.07 R')).toBeInTheDocument();
    expect(within(row).getByText('итог +4.27 R')).toBeInTheDocument();
    expect(within(row).getByText('не было')).toBeInTheDocument();
    expect(within(row).getByText('Пробой, Ложный пробой · Уровни')).toBeInTheDocument();
    expect(
      within(row).getByText(
        '5 мин, 15 мин · обе стороны · оба рынка · любой значок · R:R ≥ 1.2 · N ≥ 50',
      ),
    ).toBeInTheDocument();
    expect(within(row).getByText('300 USDT')).toBeInTheDocument();
    expect(within(row).getByText(exchangeAccount.name)).toBeInTheDocument();
  });

  // Scenario "Бюджет в списке".
  it('shows what the open trades take of the budget of an auto, or that it has none', async () => {
    fakeApi(
      routes(() => [
        autoItemOf({ ...impulse, budget: '100' }, { budgetUsed: '79.9' }),
        autoItemOf(breakouts, { budgetUsed: '30' }),
      ]),
    );
    renderApp('/idea-trades/autos');

    await screen.findByRole('table', { name: 'Автоматы' });

    expect(within(rowOf('Импульс лонг')).getByText('занято 79.90 из 100 USDT')).toBeInTheDocument();
    expect(within(rowOf('Пробои 5м')).getByText('без бюджета')).toBeInTheDocument();
  });

  it('shows the real totals of an auto with money', async () => {
    fakeApi(
      routes(() => [autoItemOf(impulse, { realClosed: 11, realResult: '9.4', refusalsToday: 3 })]),
    );
    renderApp('/idea-trades/autos');

    await screen.findByRole('table', { name: 'Автоматы' });
    const row = rowOf('Импульс лонг');

    expect(within(row).getByText('реальные ордера')).toBeInTheDocument();
    expect(within(row).getByText('11 сделок')).toBeInTheDocument();
    expect(within(row).getByText('+9.40 USDT')).toBeInTheDocument();
  });

  // Requirement "Экран «Автоматы»", scenario "Пустой список".
  it('says there are no autos and offers to add one', async () => {
    fakeApi(routes(() => []));
    renderApp('/idea-trades/autos');

    expect(await screen.findByText(/Автоматов пока нет/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '+ Добавить автомат' })).toHaveAttribute(
      'href',
      '/idea-trades/autos/new',
    );
    expect(screen.queryByRole('table', { name: 'Автоматы' })).not.toBeInTheDocument();
  });

  it('shows the way back to «Сделки» and keeps that item of the menu on', async () => {
    fakeApi(routes(() => []));
    renderApp('/idea-trades/autos');

    await screen.findByText(/Автоматов пока нет/);

    expect(screen.getByRole('navigation', { name: 'Путь' })).toHaveTextContent('Сделки / Автоматы');
    expect(
      within(screen.getByRole('navigation', { name: 'Разделы' })).getByRole('link', {
        name: 'Сделки',
      }),
    ).toHaveAttribute('aria-current', 'page');
  });

  // Requirement "Переключение режима в списке".
  it('changes the mode at once, without a question, and then the caption of the row', async () => {
    let mode = 'paper';
    const requests = fakeApi(
      routes(() => [autoItemOf({ ...breakouts, mode })], {
        'PUT /api/idea-autos/auto-3/mode': async (request) => {
          mode = ((await request.json()) as { mode: string }).mode;

          return noContent();
        },
      }),
    );
    renderApp('/idea-trades/autos');
    const user = userEvent.setup();
    await screen.findByRole('table', { name: 'Автоматы' });

    await user.click(within(rowOf('Пробои 5м')).getByRole('radio', { name: 'С деньгами' }));

    await waitFor(() =>
      expect(within(rowOf('Пробои 5м')).getByText('реальные ордера')).toBeInTheDocument(),
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(await requests.find((request) => request.method === 'PUT')!.text()).toBe(
      '{"mode":"live"}',
    );
    expect(within(rowOf('Пробои 5м')).getByRole('radio', { name: 'С деньгами' })).toBeChecked();
  });

  it('says that an auto that is off takes no ideas', async () => {
    fakeApi(routes(() => [autoItemOf({ ...breakouts, mode: 'off' })]));
    renderApp('/idea-trades/autos');

    await screen.findByRole('table', { name: 'Автоматы' });

    expect(within(rowOf('Пробои 5м')).getByText('не берёт идеи')).toBeInTheDocument();
  });

  // Requirement "Порядок перетаскиванием", scenario "Поднять автомат".
  it('saves the new order at once when the third auto is dragged to the first place', async () => {
    let order = [impulse, drops, breakouts];
    const requests = fakeApi(
      routes(() => order.map((auto) => autoItemOf(auto)), {
        'PUT /api/idea-autos/order': async (request) => {
          const { ids } = (await request.json()) as { ids: string[] };
          order = ids.map((id) => order.find((auto) => auto.id === id)!);

          return noContent();
        },
      }),
    );
    renderApp('/idea-trades/autos');
    await screen.findByRole('table', { name: 'Автоматы' });
    const data = { setData: vi.fn(), effectAllowed: '' };

    fireEvent.dragStart(screen.getByRole('button', { name: 'Перетащить «Пробои 5м»' }), {
      dataTransfer: data,
    });
    fireEvent.dragOver(rowOf('Импульс лонг'), { dataTransfer: data });
    fireEvent.drop(rowOf('Импульс лонг'), { dataTransfer: data });

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    expect(await requests.find((request) => request.method === 'PUT')!.text()).toBe(
      '{"ids":["auto-3","auto-1","auto-2"]}',
    );
    await waitFor(() =>
      expect(
        within(screen.getByRole('table', { name: 'Автоматы' }))
          .getAllByRole('row')
          .slice(1)
          .map((row) => within(row).getAllByRole('cell')[0]!.textContent?.[0]),
      ).toEqual(['1', '2', '3']),
    );
    expect(
      within(screen.getByRole('table', { name: 'Автоматы' })).getAllByRole('row')[1]!.textContent,
    ).toContain('Пробои 5м');
  });

  it('moves an auto up with the arrow on its handle', async () => {
    const requests = fakeApi(
      routes(() => [autoItemOf(impulse), autoItemOf(drops)], {
        'PUT /api/idea-autos/order': noContent,
      }),
    );
    renderApp('/idea-trades/autos');
    await screen.findByRole('table', { name: 'Автоматы' });

    fireEvent.keyDown(screen.getByRole('button', { name: 'Перетащить «Падения шорт»' }), {
      key: 'ArrowUp',
    });

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    expect(await requests.find((request) => request.method === 'PUT')!.text()).toBe(
      '{"ids":["auto-2","auto-1"]}',
    );
  });

  it('keeps the order when a row is dropped on itself', async () => {
    const requests = fakeApi(routes(() => [autoItemOf(impulse), autoItemOf(drops)]));
    renderApp('/idea-trades/autos');
    await screen.findByRole('table', { name: 'Автоматы' });
    const data = { setData: vi.fn(), effectAllowed: '' };

    fireEvent.dragStart(screen.getByRole('button', { name: 'Перетащить «Импульс лонг»' }), {
      dataTransfer: data,
    });
    fireEvent.drop(rowOf('Импульс лонг'), { dataTransfer: data });

    expect(requests.some((request) => request.method === 'PUT')).toBe(false);
    expect(createEvent.dragOver(rowOf('Импульс лонг')).defaultPrevented).toBe(false);
  });

  // Requirement "Удаление автомата на экране", scenario "Отказ от удаления".
  it('asks before a delete, says what stays, and keeps the auto when the owner declines', async () => {
    const requests = fakeApi(routes(() => [autoItemOf(impulse)]));
    renderApp('/idea-trades/autos');
    const user = userEvent.setup();
    await screen.findByRole('table', { name: 'Автоматы' });

    await user.click(within(rowOf('Импульс лонг')).getByRole('button', { name: /Удалить/ }));
    const dialog = await screen.findByRole('dialog', { name: 'Удалить автомат «Импульс лонг»?' });

    expect(dialog).toHaveTextContent('Сделки автомата остаются');
    expect(dialog).toHaveTextContent('открытые идут до своего исхода');

    await user.click(within(dialog).getByRole('button', { name: 'Не удалять' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(rowOf('Импульс лонг')).toBeInTheDocument();
    expect(requests.some((request) => request.method === 'DELETE')).toBe(false);
  });

  it('deletes the auto when the owner confirms, and reads the list again', async () => {
    let list = [autoItemOf(impulse)];
    const requests = fakeApi(
      routes(() => list, {
        'DELETE /api/idea-autos/auto-1': () => {
          list = [];

          return noContent();
        },
      }),
    );
    renderApp('/idea-trades/autos');
    const user = userEvent.setup();
    await screen.findByRole('table', { name: 'Автоматы' });

    await user.click(within(rowOf('Импульс лонг')).getByRole('button', { name: /Удалить/ }));
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    expect(await screen.findByText(/Автоматов пока нет/)).toBeInTheDocument();
    expect(requests.some((request) => request.method === 'DELETE')).toBe(true);
  });

  it('explains under the table which auto takes a pair', async () => {
    fakeApi(routes(() => [autoItemOf(impulse)]));
    renderApp('/idea-trades/autos');

    expect(
      await screen.findByText(/Пару на одном аккаунте берёт верхний автомат с деньгами/),
    ).toBeInTheDocument();
  });

  // Requirement "Отказы сегодня в списке".
  it('leads the count of refusals of an auto to the journal with the filter of that auto', async () => {
    fakeApi(routes(() => [autoItemOf(impulse, { refusalsToday: 3 }), autoItemOf(drops)]));
    renderApp('/idea-trades/autos');
    await screen.findByRole('table', { name: 'Автоматы' });

    const link = within(rowOf('Импульс лонг')).getByRole('link', { name: /Отказов сегодня/ });

    expect(link).toHaveTextContent('3');
    expect(link).toHaveAttribute('href', '/idea-trades?autoId=auto-1');
    expect(within(rowOf('Падения шорт')).queryByRole('link', { name: /Отказов/ })).toBeNull();
  });

  it('leads «Изменить» to the form of the auto', async () => {
    fakeApi(routes(() => [autoItemOf(impulse)]));
    renderApp('/idea-trades/autos');
    await screen.findByRole('table', { name: 'Автоматы' });

    expect(within(rowOf('Импульс лонг')).getByRole('link', { name: /Изменить/ })).toHaveAttribute(
      'href',
      '/idea-trades/autos/auto-1',
    );
  });

  // Requirement "Строка защит на «Автоматах»", scenario "Состояние защит".
  it('shows the state of the guards in a line with the way to their settings', async () => {
    fakeApi(routes(() => []));
    renderApp('/idea-trades/autos');

    const strip = await screen.findByRole('region', { name: 'Защиты' });

    await waitFor(() =>
      expect(strip).toHaveTextContent(
        'открыто 3 из 8 · риск 10.22 из 40 USDT · сегодня -19.05 из 30 USDT · в лонг 2 · в шорт 1 из 5 · бюджет автоматов 180.00 из 200 USDT',
      ),
    );
    expect(within(strip).getByRole('link', { name: 'Торговля идеями →' })).toHaveAttribute(
      'href',
      '/idea-trades/settings',
    );
  });

  it('says that a guard that is off is off', async () => {
    fakeApi({
      ...routes(() => []),
      'GET /api/idea-trading/guards': () =>
        json(guardsOf({ dailyLossLimit: null, maxSameDirection: null, autosBudget: null })),
    });
    renderApp('/idea-trades/autos');

    const strip = await screen.findByRole('region', { name: 'Защиты' });

    await waitFor(() =>
      expect(strip).toHaveTextContent(
        'сегодня -19.05 USDT · выкл · в лонг 2 · в шорт 1 выкл · бюджет автоматов 180.00 USDT · выкл',
      ),
    );
  });

  // Requirement "Баннер дневного стопа".
  it('shows the banner of the daily stop while it holds', async () => {
    fakeApi({
      ...routes(() => []),
      'GET /api/idea-trading/guards': () =>
        json(
          guardsOf({
            daySum: '-30.15',
            triggeredAt: '2026-11-07T15:12:00Z',
            resumesAt: '2026-11-08T00:00:00Z',
          }),
        ),
    });
    renderApp('/idea-trades/autos');

    const banner = await screen.findByRole('status', { name: 'Дневной стоп' });

    expect(banner).toHaveTextContent('Дневной стоп сработал — автоматы не входят до 00:00 UTC');
    expect(banner).toHaveTextContent('Сегодня -30.15 из 30 USDT · сработал в 15:12 UTC');
    expect(banner).toHaveTextContent('ручные входы и открытые сделки работают как обычно');
  });

  it('shows no banner while the stop has not held', async () => {
    fakeApi(routes(() => []));
    renderApp('/idea-trades/autos');
    await screen.findByRole('region', { name: 'Защиты' });

    expect(screen.queryByRole('status', { name: 'Дневной стоп' })).not.toBeInTheDocument();
  });

  it('says so when the autos do not load', async () => {
    fakeApi({ 'GET /api/idea-trading/guards': () => json(guardsOf()) });
    renderApp('/idea-trades/autos');

    expect(await screen.findByText('Не удалось загрузить автоматы.')).toBeInTheDocument();
  });
});
