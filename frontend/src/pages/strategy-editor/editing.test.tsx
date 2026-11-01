import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { openEma, selectNode as select } from '@/test/openEditor';

describe('editing the graph', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Несовместимые типы": a numeric input is offered only numeric outputs.
  it('offers an input only outputs of its type', async () => {
    openEma();

    const card = await select('Рыночный ордер');
    const size = within(card).getByRole('combobox', { name: 'Источник size' });

    const options = within(size)
      .getAllByRole('option')
      .map((option) => option.textContent);
    expect(options).toContain('params.amount');
    expect(options).toContain('candle.close');
    expect(options).not.toContain('cross.up');
  });

  it('disconnects an input and takes its value on the node', async () => {
    const { container } = openEma();

    const card = await select('Рыночный ордер');
    await userEvent.selectOptions(
      within(card).getByRole('combobox', { name: 'Источник size' }),
      '',
    );

    await waitFor(() => expect(container.querySelectorAll('.react-flow__edge')).toHaveLength(8));
    const value = screen.getByRole('textbox', { name: 'Значение enter.size' });
    await userEvent.type(value, '5');
    await userEvent.tab();
    expect(screen.getByRole('textbox', { name: 'Значение enter.size' })).toHaveValue('5');
  });

  it('connects an input to another output', async () => {
    const { container } = openEma();

    const card = await select('Рыночный ордер');
    await userEvent.selectOptions(
      within(card).getByRole('combobox', { name: 'Источник size' }),
      'candle.close',
    );

    await waitFor(() =>
      expect(
        container.querySelector('.react-flow__edge[data-id="candle.close->enter.size"]'),
      ).not.toBeNull(),
    );
    expect(
      container.querySelector('.react-flow__edge[data-id="params.amount->enter.size"]'),
    ).toBeNull();
  });

  it('removes the selected node with its wires', async () => {
    const { container } = openEma();

    const card = await select('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));

    await waitFor(() =>
      expect(screen.queryByRole('group', { name: 'НЕ' })).not.toBeInTheDocument(),
    );
    expect(container.querySelectorAll('.react-flow__edge')).toHaveLength(7);
  });
});
