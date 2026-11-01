import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { openEma } from '@/test/openEditor';

async function openCard() {
  await userEvent.click(await screen.findByRole('button', { name: 'Параметры' }));

  return screen.getByRole('region', { name: 'Стратегия' });
}

describe('strategy card', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('shows the name, the direction and the parameters', async () => {
    openEma();

    const card = await openCard();

    expect(within(card).getByRole('textbox', { name: 'Название' })).toHaveValue('EMA');
    expect(within(card).getByRole('radio', { name: 'Лонг' })).toBeChecked();
    expect(
      within(card)
        .getAllByRole('textbox', { name: 'Имя параметра' })
        .map((input) => (input as HTMLInputElement).value),
    ).toEqual(['fast', 'slow', 'amount']);
  });

  // Scenario "Новый параметр".
  it('gives the parameters node an output for a new parameter', async () => {
    openEma();

    const card = await openCard();
    await userEvent.click(within(card).getByRole('button', { name: '+ Добавить' }));
    const name = within(card).getAllByRole('textbox', { name: 'Имя параметра' }).at(-1)!;
    await userEvent.clear(name);
    await userEvent.type(name, 'stop');
    await userEvent.tab();

    const parameters = screen.getByRole('group', { name: 'Параметры' });
    await waitFor(() => expect(within(parameters).getByText('stop')).toBeInTheDocument());
  });

  it('renames a parameter keeping its wires', async () => {
    const { container } = openEma();

    const card = await openCard();
    const fast = within(card).getAllByRole('textbox', { name: 'Имя параметра' })[0]!;
    await userEvent.clear(fast);
    await userEvent.type(fast, 'quick');
    await userEvent.tab();

    await waitFor(() =>
      expect(
        container.querySelector('.react-flow__edge[data-id="params.quick->cross.fast"]'),
      ).not.toBeNull(),
    );
  });

  it('removes a parameter with its wires', async () => {
    const { container } = openEma();

    const card = await openCard();
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить параметр amount' }));

    await waitFor(() => expect(container.querySelectorAll('.react-flow__edge')).toHaveLength(8));
  });

  it('changes the direction', async () => {
    openEma();

    const card = await openCard();
    await userEvent.click(within(card).getByRole('radio', { name: 'Шорт' }));

    expect(within(card).getByRole('radio', { name: 'Шорт' })).toBeChecked();
    expect(screen.getByRole('banner', { name: 'Стратегия' })).toHaveTextContent('Шорт');
  });
});
