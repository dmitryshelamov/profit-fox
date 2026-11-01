import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { json } from '@/test/fakeApi';
import { emaSummary } from '@/test/fixtures';
import { openEma, selectNode } from '@/test/openEditor';

/** A check that finds the input `enter.when` unconnected whenever its wire is missing, as the server would. */
const checkWhen = {
  [`POST /api/strategies/${emaSummary.id}/validate`]: async (request: Request) => {
    const { definition } = (await request.json()) as { definition: { wires: { to: string }[] } };
    const connected = definition.wires.some((wire) => wire.to === 'enter.when');

    return json({
      errors: connected ? {} : { 'graph.enter.when': ['Input is not connected and has no value.'] },
      usages: [],
      bots: [],
    });
  },
};

describe('errors of the check', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Неподключённый вход".
  it('marks the node and the pin of an error and lists it', async () => {
    openEma(checkWhen);

    const card = await selectNode('Рыночный ордер');
    await userEvent.selectOptions(
      within(card).getByRole('combobox', { name: 'Источник when' }),
      '',
    );

    const node = screen.getByRole('group', { name: 'Рыночный ордер' });
    await waitFor(() => expect(node).toHaveAttribute('aria-invalid', 'true'));
    expect(within(node).getByText('when')).toHaveClass('text-err');
    const errors = screen.getByRole('button', { name: /Ошибки/ });
    expect(errors).toHaveTextContent('1');

    await userEvent.click(errors);
    const sheet = screen.getByRole('region', { name: 'Ошибки' });
    expect(sheet).toHaveTextContent('graph.enter.when');
    expect(sheet).toHaveTextContent('Input is not connected and has no value.');
    await userEvent.click(within(sheet).getByRole('button', { name: 'Показать на холсте' }));
    expect(screen.getByRole('region', { name: 'Узел: Рыночный ордер' })).toBeInTheDocument();
  });

  // Scenario "Ошибка исправлена".
  it('clears the error once the input is connected again', async () => {
    openEma(checkWhen);

    const card = await selectNode('Рыночный ордер');
    const when = within(card).getByRole('combobox', { name: 'Источник when' });
    await userEvent.selectOptions(when, '');
    const node = screen.getByRole('group', { name: 'Рыночный ордер' });
    await waitFor(() => expect(node).toHaveAttribute('aria-invalid', 'true'));

    await userEvent.selectOptions(
      within(screen.getByRole('region', { name: 'Узел: Рыночный ордер' })).getByRole('combobox', {
        name: 'Источник when',
      }),
      'entry.result',
    );

    await waitFor(() => expect(node).toHaveAttribute('aria-invalid', 'false'));
    expect(screen.getByRole('button', { name: /Ошибки/ })).toHaveTextContent('0');
  });

  it('reports a check that names bots the change would break', async () => {
    openEma({
      [`POST /api/strategies/${emaSummary.id}/validate`]: () =>
        json({
          errors: {},
          usages: [],
          bots: [
            { id: 'b', name: 'EMA BTC', errors: { 'parameterValues.fast': 'No such parameter.' } },
          ],
        }),
    });

    const card = await selectNode('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Ошибки/ })).toHaveTextContent('1'),
    );
    await userEvent.click(screen.getByRole('button', { name: /Ошибки/ }));
    expect(screen.getByRole('region', { name: 'Ошибки' })).toHaveTextContent('EMA BTC');
  });
});
