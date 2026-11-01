import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { openEma, selectNode } from '@/test/openEditor';

describe('node card', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Настройки рыночного ордера": the node shows the setting chosen in its card.
  it('changes the size of a market order', async () => {
    openEma();

    const card = await selectNode('Рыночный ордер');
    const size = within(card).getByRole('radiogroup', { name: 'Размер' });
    await userEvent.click(within(size).getByRole('radio', { name: 'Объём' }));

    expect(within(size).getByRole('radio', { name: 'Объём' })).toBeChecked();
    expect(screen.getByRole('group', { name: 'Рыночный ордер' })).toHaveTextContent(
      'Покупка · Объём',
    );
  });

  it('shows what each input is wired to', async () => {
    openEma();

    const card = await selectNode('Рыночный ордер');

    expect(within(card).getByRole('combobox', { name: 'Источник when' })).toHaveValue(
      'entry.result',
    );
    expect(within(card).getByRole('combobox', { name: 'Источник size' })).toHaveValue(
      'params.amount',
    );
  });

  it('finds a node on the canvas by its name and selects it', async () => {
    openEma();

    await userEvent.click(await screen.findByRole('button', { name: 'Найти узел на холсте' }));
    await userEvent.type(
      screen.getByRole('searchbox', { name: 'Узел на холсте' }),
      'позиция{Enter}',
    );

    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Узел: Позиция' })).toBeInTheDocument(),
    );
  });
});
