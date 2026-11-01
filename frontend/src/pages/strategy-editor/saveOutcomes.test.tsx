import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { problem } from '@/test/fakeApi';
import { emaSummary } from '@/test/fixtures';
import { emaDefinition, openEma, selectNode } from '@/test/openEditor';

const put = `PUT /api/strategies/${emaSummary.id}`;

async function removeNotNode() {
  const card = await selectNode('НЕ');
  await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled());
}

describe('outcomes of a save', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('saves the name and the definition with its layout', async () => {
    const { requests } = openEma({ [put]: () => new Response(null, { status: 204 }) });

    await removeNotNode();
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeDisabled());
    const body = (await requests.find((request) => request.method === 'PUT')!.json()) as {
      name: string;
      definition: typeof emaDefinition;
    };
    expect(body.name).toBe('EMA');
    expect(body.definition.nodes.map((node) => node.id)).not.toContain('flat');
    expect(body.definition.layout).toEqual(emaDefinition.layout);
  });

  it('saves the limits of a parameter as edited', async () => {
    const { requests } = openEma({ [put]: () => new Response(null, { status: 204 }) });

    await userEvent.click(await screen.findByRole('button', { name: 'Параметры' }));
    const card = screen.getByRole('region', { name: 'Стратегия' });
    await userEvent.clear(within(card).getByRole('textbox', { name: 'min fast' }));
    await userEvent.type(within(card).getByRole('textbox', { name: 'min fast' }), '3');
    await userEvent.tab();
    await userEvent.click(within(card).getAllByRole('checkbox', { name: 'целое' })[0]!);
    await userEvent.selectOptions(
      within(card).getByRole('combobox', { name: 'Тип amount' }),
      'duration',
    );
    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    const body = (await requests.find((request) => request.method === 'PUT')!.json()) as {
      definition: { parameters: unknown[] };
    };
    expect(body.definition.parameters[0]).toEqual({
      name: 'fast',
      type: 'number',
      default: 12,
      min: 3,
      max: 200,
    });
    expect(body.definition.parameters[2]).toEqual({ name: 'amount', type: 'duration' });
  });

  // Scenario "Правка ломает бота".
  it('names the bots a rejected save would break and keeps the edits', async () => {
    openEma({
      [put]: () =>
        problem(409, 'Bots of the strategy stand in the way.', {
          usages: [],
          bots: [
            { id: 'b', name: 'EMA BTC', errors: { 'parameterValues.fast': 'No such parameter.' } },
          ],
        }),
    });

    await removeNotNode();
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    const dialog = await screen.findByRole('dialog', { name: 'Правка не сохранена' });
    expect(dialog).toHaveTextContent('EMA BTC');
    expect(dialog).toHaveTextContent('parameterValues.fast');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Вернуться к правке' }));
    expect(screen.queryByRole('group', { name: 'НЕ' })).not.toBeInTheDocument();
  });

  // Scenario "Конфликт ревизии".
  it('reports a save that met a change of the library and lets it be repeated', async () => {
    let attempts = 0;
    openEma({
      [put]: () =>
        ++attempts === 1
          ? problem(409, 'The library has changed since it was read. Repeat the request.', {
              usages: [],
            })
          : new Response(null, { status: 204 }),
    });

    await removeNotNode();
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Библиотеку изменили одновременно с сохранением',
    );
    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeDisabled());
    expect(attempts).toBe(2);
  });

  it('shows the errors of a save rejected after the check', async () => {
    openEma({
      [put]: () =>
        problem(400, 'Invalid', {
          errors: { 'graph.enter.when': ['Input is not connected and has no value.'] },
        }),
    });

    await removeNotNode();
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Ошибки/ })).toHaveTextContent('1'),
    );
    expect(screen.getByRole('group', { name: 'Рыночный ордер' })).toHaveAttribute(
      'aria-invalid',
      'true',
    );
  });

  it('reports a save that did not reach the server and keeps the edits', async () => {
    openEma({ [put]: () => Promise.reject(new TypeError('Failed to fetch')) });

    await removeNotNode();
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    const status = await screen.findByRole('status');
    expect(status).toHaveTextContent('Сохранить не удалось');
    expect(status).toHaveTextContent('Failed to fetch');
    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled();
  });
});
