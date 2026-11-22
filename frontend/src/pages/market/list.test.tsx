import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { marketList, marketQueries } from '@/test/market';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

function routes(total = 831) {
  return {
    ...baseRoutes(),
    'GET /api/strategies': () => json([]),
    'GET /api/market/pairs': marketList(total),
  };
}

const last = (requests: readonly Request[]) => marketQueries(requests).at(-1)!;
const head = (name: string) => screen.getByRole('columnheader', { name: new RegExp(`^${name}`) });

// Spec bench-ui «Сортировка по колонке».
describe('the sort of a list on the server', () => {
  // Scenario «Первое нажатие».
  it('sorts a column of numbers from the largest on the first click and marks only its head', async () => {
    const requests = fakeApi(routes());
    renderApp('/market');
    await screen.findByRole('row', { name: 'P831USDT' });
    expect(head('Оборот 24 ч')).toHaveAttribute('aria-sort', 'descending');

    await userEvent.click(within(head('Фандинг')).getByRole('button'));

    await waitFor(() => expect(last(requests).get('sort')).toBe('fundingRate'));
    expect(last(requests).get('direction')).toBe('desc');
    expect(head('Фандинг')).toHaveAttribute('aria-sort', 'descending');
    expect(within(head('Фандинг')).getByRole('button')).toHaveTextContent('↓');
    expect(head('Оборот 24 ч')).not.toHaveAttribute('aria-sort');
  });

  // Scenarios «Повторное нажатие» and «Текстовая колонка».
  it('turns the direction on a second click and sorts text from A', async () => {
    const requests = fakeApi(routes());
    renderApp('/market');
    await screen.findByRole('row', { name: 'P831USDT' });

    await userEvent.click(within(head('Оборот 24 ч')).getByRole('button'));
    await screen.findByRole('row', { name: 'P001USDT' });
    expect(last(requests).get('direction')).toBe('asc');
    expect(within(head('Оборот 24 ч')).getByRole('button')).toHaveTextContent('↑');

    await userEvent.click(within(head('Пара')).getByRole('button'));
    await waitFor(() => expect(last(requests).get('sort')).toBe('symbol'));
    expect(last(requests).get('direction')).toBe('asc');
  });

  // Scenario «Выбор после перезагрузки».
  it('keeps the sort of the table in the browser', async () => {
    const requests = fakeApi(routes());
    const first = renderApp('/market');
    await screen.findByRole('row', { name: 'P831USDT' });
    await userEvent.click(within(head('Фандинг')).getByRole('button'));
    await userEvent.click(within(head('Фандинг')).getByRole('button'));
    await waitFor(() => expect(last(requests).get('direction')).toBe('asc'));
    first.unmount();

    const again = fakeApi(routes());
    renderApp('/market');
    await screen.findByRole('row', { name: 'P001USDT' });
    expect(marketQueries(again)[0]!.get('sort')).toBe('fundingRate');
    expect(marketQueries(again)[0]!.get('direction')).toBe('asc');
  });
});

// Spec bench-ui «Страницы списка».
describe('the pages of a list', () => {
  // Scenario «Вторая страница».
  it('shows rows 51 to 100 of 831 on the next page and puts the page in the address', async () => {
    fakeApi(routes());
    const { history } = renderApp('/market');
    await screen.findByRole('row', { name: 'P831USDT' });
    const pages = screen.getByRole('navigation', { name: 'Страницы' });
    expect(pages).toHaveTextContent('1–50 из 831');

    await userEvent.click(within(pages).getByRole('button', { name: 'Следующая страница' }));

    await screen.findByRole('row', { name: 'P781USDT' });
    expect(pages).toHaveTextContent('51–100 из 831');
    expect(pages).toHaveTextContent('2 из 17');
    expect(history.location.search).toContain('page=2');
  });

  // Scenario «Смена размера».
  it('opens the first of five pages when the size becomes 200 on the third', async () => {
    const requests = fakeApi(routes());
    const { history } = renderApp('/market?page=3');
    await screen.findByRole('row', { name: 'P731USDT' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'На странице' }), '200');

    await screen.findByRole('row', { name: 'P632USDT' });
    expect(screen.getByRole('navigation', { name: 'Страницы' })).toHaveTextContent('1 из 5');
    expect(last(requests).get('pageSize')).toBe('200');
    expect(screen.getAllByRole('row')).toHaveLength(201);
    expect(history.location.search).not.toContain('page=');
  });

  // Scenario «Смена сортировки на второй странице».
  it('opens the first page when another column is sorted on the second', async () => {
    const requests = fakeApi(routes());
    const { history } = renderApp('/market?page=2');
    await screen.findByRole('row', { name: 'P781USDT' });

    await userEvent.click(within(head('Цена')).getByRole('button'));

    await waitFor(() => expect(last(requests).get('sort')).toBe('price'));
    expect(last(requests).get('page')).toBe('1');
    expect(history.location.search).not.toContain('page=');
  });

  // Scenario «Последняя страница».
  it('has no next page on page 17 of 17', async () => {
    fakeApi(routes());
    renderApp('/market?page=17');
    await screen.findByRole('row', { name: 'P001USDT' });

    const pages = screen.getByRole('navigation', { name: 'Страницы' });
    expect(pages).toHaveTextContent('801–831 из 831');
    expect(within(pages).getByRole('button', { name: 'Следующая страница' })).toBeDisabled();
    expect(within(pages).getByRole('button', { name: 'Предыдущая страница' })).toBeEnabled();
  });
});
