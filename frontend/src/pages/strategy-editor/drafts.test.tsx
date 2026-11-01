import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { draftKey, drafts } from '@/modules/graph-editor';
import { emaSummary } from '@/test/fixtures';
import { emaDefinition, openEma, selectNode } from '@/test/openEditor';

const key = draftKey({ strategy: emaSummary.id });

function storeDraftWithTwoMoreNodes() {
  drafts.save(
    key,
    {
      name: 'EMA',
      definition: {
        ...(emaDefinition as never as { nodes: { id: string; kind: string }[] }),
        nodes: [...emaDefinition.nodes, { id: 'ema1', kind: 'ema' }, { id: 'ema2', kind: 'ema' }],
      } as never,
    },
    new Date('2026-11-01T14:32:00Z'),
  );
}

describe('drafts in the editor', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Восстановление после закрытия вкладки".
  it('restores a draft left in the browser', async () => {
    storeDraftWithTwoMoreNodes();
    openEma();

    const dialog = await screen.findByRole('dialog', { name: 'Найден несохранённый черновик' });
    expect(dialog).toHaveTextContent('узлы: +2');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Восстановить' }));

    await waitFor(() => expect(screen.getAllByRole('group', { name: 'EMA' })).toHaveLength(2));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled());
  });

  // Scenario "Отказ от черновика".
  it('drops a draft and shows the saved strategy', async () => {
    storeDraftWithTwoMoreNodes();
    openEma();

    const dialog = await screen.findByRole('dialog', { name: 'Найден несохранённый черновик' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Отбросить черновик' }));

    expect(screen.queryByRole('group', { name: 'EMA' })).not.toBeInTheDocument();
    expect(drafts.read(key)).toBeUndefined();
  });

  it('drops silently a draft that differs from the saved strategy only in positions', async () => {
    drafts.save(
      key,
      { name: 'EMA', definition: { ...emaDefinition, layout: {} } as never },
      new Date(),
    );
    openEma();

    await screen.findByRole('group', { name: 'Свеча' });

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(drafts.read(key)).toBeUndefined();
  });

  it('keeps every edit as a draft and drops the draft once saved', async () => {
    openEma({
      [`PUT /api/strategies/${emaSummary.id}`]: () => new Response(null, { status: 204 }),
    });

    const card = await selectNode('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));

    await waitFor(() =>
      expect(drafts.read(key)?.definition.nodes.map((node) => node.id)).not.toContain('flat'),
    );
    await waitFor(() => expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
    await waitFor(() => expect(drafts.read(key)).toBeUndefined());
  });

  it('drops the draft with the edits', async () => {
    openEma();

    const card = await selectNode('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));
    await waitFor(() => expect(drafts.read(key)).toBeDefined());
    await userEvent.click(screen.getByRole('button', { name: 'Отменить правки' }));

    expect(drafts.read(key)).toBeUndefined();
  });

  // Scenario "Уход с несохранёнными правками".
  it('asks before leaving unsaved edits', async () => {
    const { history } = openEma({
      'GET /api/strategies': () =>
        new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }),
    });

    const card = await selectNode('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));
    await userEvent.click(screen.getByRole('link', { name: '‹ Стратегии' }));

    const dialog = await screen.findByRole('dialog', { name: 'Уйти без сохранения?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Остаться' }));
    expect(history.location.pathname).toBe(`/strategies/${emaSummary.id}`);

    await userEvent.click(screen.getByRole('link', { name: '‹ Стратегии' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Уйти без сохранения?' })).getByRole(
        'button',
        { name: 'Уйти' },
      ),
    );
    await waitFor(() => expect(history.location.pathname).toBe('/strategies'));
  });

  it('asks the browser to confirm closing the tab with unsaved edits', async () => {
    openEma();

    const card = await selectNode('НЕ');
    await userEvent.click(within(card).getByRole('button', { name: 'Удалить узел' }));

    const event = new Event('beforeunload', { cancelable: true });
    fireEvent(window, event);
    expect(event.defaultPrevented).toBe(true);
  });
});
