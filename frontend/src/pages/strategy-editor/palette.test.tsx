import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import catalog from '@/test/catalog.fixture.json';
import { fakeApi, json } from '@/test/fakeApi';
import { emaSummary, savedStrategy, trendFilter } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

import ema from '../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';

describe('node palette', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function open() {
    fakeApi({
      [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, ema)),
      'GET /api/strategy-nodes': () => json([...catalog, trendFilter]),
    });

    return renderApp(`/strategies/${emaSummary.id}`);
  }

  it('offers the library by groups, without the pins of composite nodes the strategy cannot have', async () => {
    open();

    await userEvent.click(await screen.findByRole('button', { name: 'Добавить узел' }));

    const palette = screen.getByRole('dialog', { name: 'Библиотека узлов' });
    expect(within(palette).getByRole('group', { name: 'Ордера' })).toHaveTextContent(
      'Держать лимитку',
    );
    expect(within(palette).getByRole('group', { name: 'Составные' })).toHaveTextContent(
      'Фильтр тренда',
    );
    expect(within(palette).queryByRole('button', { name: 'Входы узла' })).not.toBeInTheDocument();
  });

  it('adds the clicked node with the default settings of the catalog', async () => {
    open();

    await userEvent.click(await screen.findByRole('button', { name: 'Добавить узел' }));
    await userEvent.click(screen.getByRole('button', { name: 'Держать лимитку' }));

    const node = await screen.findByRole('group', { name: 'Держать лимитку' });
    expect(node).toHaveTextContent('Покупка · Объём');
    expect(within(node).getByText('price')).toBeInTheDocument();
  });

  it('finds a node of the library by its name', async () => {
    open();

    await userEvent.click(await screen.findByRole('button', { name: 'Добавить узел' }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Найти узел' }), 'лим');

    const palette = screen.getByRole('dialog', { name: 'Библиотека узлов' });
    expect(
      within(palette)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Держать лимитку']);
  });
});
