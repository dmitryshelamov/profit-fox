import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { json, problem } from '@/test/fakeApi';
import { emaSummary } from '@/test/fixtures';
import { openEma, selectNode } from '@/test/openEditor';

const save = () => screen.getByRole('button', { name: 'Сохранить' });

describe('saving', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Сохранение без изменений".
  it('cannot save without edits', async () => {
    openEma();

    await screen.findByRole('group', { name: 'Свеча' });

    expect(save()).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Отменить правки' })).toBeDisabled();
  });

  // Scenario "Есть ошибка".
  it('cannot save while the check finds an error', async () => {
    openEma({
      [`POST /api/strategies/${emaSummary.id}/validate`]: () =>
        json({
          errors: { 'graph.enter.when': ['Input is not connected and has no value.'] },
          usages: [],
          bots: [],
        }),
    });

    const card = await selectNode('Рыночный ордер');
    await userEvent.selectOptions(
      within(card).getByRole('combobox', { name: 'Источник when' }),
      '',
    );

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Ошибки/ })).toHaveTextContent('1'),
    );
    expect(save()).toBeDisabled();
  });

  it('can save a checked edit without errors, and not before the check answers', async () => {
    openEma();

    const card = await selectNode('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));

    expect(save()).toBeDisabled();
    await waitFor(() => expect(save()).toBeEnabled());
  });

  it('drops the edits and returns to the saved strategy', async () => {
    openEma();

    const card = await selectNode('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));
    await userEvent.click(screen.getByRole('button', { name: 'Отменить правки' }));

    expect(await screen.findByRole('group', { name: 'НЕ' })).toBeInTheDocument();
    expect(save()).toBeDisabled();
  });

  it('cannot save a strategy without a name', async () => {
    openEma({
      [`POST /api/strategies/${emaSummary.id}/validate`]: () =>
        problem(400, 'One or more validation errors occurred.', {
          errors: { name: ['The Name field is required.'] },
        }),
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Параметры' }));
    await userEvent.clear(
      within(screen.getByRole('region', { name: 'Стратегия' })).getByRole('textbox', {
        name: 'Название',
      }),
    );

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Ошибки/ })).toHaveTextContent('1'),
    );
    expect(save()).toBeDisabled();
  });

  it('stops waiting for a check that failed, says so and lets the server check on save', async () => {
    let attempts = 0;
    openEma({
      [`POST /api/strategies/${emaSummary.id}/validate`]: () =>
        ++attempts === 1 ? problem(502, 'Bad Gateway') : json({ errors: {}, usages: [], bots: [] }),
    });

    const card = await selectNode('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));

    const notice = await screen.findByRole('status');
    expect(notice).toHaveTextContent('Проверка не удалась');
    expect(save()).toBeEnabled();
    await userEvent.click(within(notice).getByRole('button', { name: 'Повторить проверку' }));
    await waitFor(() => expect(attempts).toBe(2));
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
