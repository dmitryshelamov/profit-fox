import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { draftKey, readDraft, writeDraft } from '@/modules/graph-editor/drafts/drafts';
import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { h0001Graph } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

const signal = (name: string, more: object = {}) => ({
  name,
  description: null,
  version: 1,
  error: null,
  id: `id-${name}`,
  strategies: [],
  ...more,
});

const rowOf = (name: string) => screen.findByRole('row', { name: `Сигнал ${name}` });

/** Opens the menu of a row and chooses one of its items. */
async function choose(row: HTMLElement, item: string) {
  await userEvent.click(within(row).getByRole('button', { name: /^Действия / }));
  await userEvent.click(screen.getByRole('menuitem', { name: item }));
}

const deletes = (requests: readonly Request[]) =>
  requests.filter((request) => request.method === 'DELETE');

describe('the section «Сигналы»', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('opens on the root path', async () => {
    fakeApi(baseRoutes());
    const { history } = renderApp('/');

    expect(await screen.findByRole('heading', { name: 'Сигналы' })).toBeInTheDocument();
    expect(history.location.pathname).toBe('/signals');
  });

  // Scenarios «Сигнал в списке» and «Испорченный файл».
  it('lists the signals in the order the API gives and a broken file with its error', async () => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () =>
        json([
          { name: 'broken', description: null, version: 1, error: 'graph is required' },
          { name: 'candle-drop', description: 'свеча', version: 1, error: null },
          { name: 'funding-high', description: null, version: 1, error: null },
        ]),
    });
    renderApp('/signals');

    const list = await screen.findByRole('region', { name: 'Сохранённые сигналы' });
    await waitFor(() =>
      expect(within(list).getAllByRole('row', { name: /^Сигнал / })).toHaveLength(3),
    );
    expect(
      within(list)
        .getAllByRole('row', { name: /^Сигнал / })
        .map((item) => item.getAttribute('aria-label')),
    ).toEqual(['Сигнал broken', 'Сигнал candle-drop', 'Сигнал funding-high']);
    expect(within(list).getAllByText('версия 1')).toHaveLength(3);
    expect(within(list).getByText(/Файл не читается: graph is required/)).toBeInTheDocument();
    expect(within(await rowOf('broken')).getByText('не читается')).toHaveAttribute(
      'data-tone',
      'err',
    );
    expect(screen.getByRole('link', { name: /H0001 funding-short-4h/ })).toHaveAttribute(
      'href',
      '/hypotheses/H0001',
    );
  });

  // Scenario «Удаление»; spec bench-ui «Меню строки», scenario «Отмена удаления».
  it('deletes a signal only after the confirmation in the dialog', async () => {
    let deleted = false;
    const requests = fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json(deleted ? [] : [signal('price-drop')]),
      'DELETE /api/signals/price-drop': () => {
        deleted = true;
        return new Response(null, { status: 204 });
      },
    });
    const confirm = vi.spyOn(window, 'confirm');
    renderApp('/signals');

    await choose(await rowOf('price-drop'), 'Удалить');
    const question = screen.getByRole('dialog', { name: 'Удалить сигнал price-drop' });
    expect(question).toHaveTextContent(
      'Удалить сигнал price-drop? Незакоммиченный файл не вернуть.',
    );
    await userEvent.click(within(question).getByRole('button', { name: 'Отмена' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(deletes(requests)).toHaveLength(0);

    await choose(await rowOf('price-drop'), 'Удалить');
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(deletes(requests)).toHaveLength(0);
    expect(await rowOf('price-drop')).toBeInTheDocument();

    await choose(await rowOf('price-drop'), 'Удалить');
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    await waitFor(() => expect(deletes(requests)).toHaveLength(1));
    expect(await screen.findByText('Сигналов нет.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(confirm).not.toHaveBeenCalled();
  });

  // Spec signal-constructor «Сигнал, на который ссылается стратегия»: the refusal names the strategies in the dialog.
  it('keeps the dialog of the delete open with the strategies that use the signal', async () => {
    writeDraft(draftKey({ name: 'funding-high' }), {
      description: '',
      graph: h0001Graph(),
      layout: {},
    });
    const requests = fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json([signal('funding-high', { strategies: ['funding-short'] })]),
      'DELETE /api/signals/funding-high': () =>
        json(
          {
            errors: [{ code: 'SignalInUse', reason: 'the signal funding-high is used' }],
            strategies: ['funding-short', 'price-drop-long'],
          },
          409,
        ),
    });
    renderApp('/signals');

    await choose(await rowOf('funding-high'), 'Удалить');
    const question = screen.getByRole('dialog', { name: 'Удалить сигнал funding-high' });
    await userEvent.click(within(question).getByRole('button', { name: 'Удалить' }));

    expect(await within(question).findByRole('alert')).toHaveTextContent(
      'Сигнал используют стратегии: funding-short, price-drop-long',
    );
    expect(question).toBeVisible();
    expect(deletes(requests)).toHaveLength(1);
    expect(await rowOf('funding-high')).toBeInTheDocument();
    expect(readDraft(draftKey({ name: 'funding-high' }))).not.toBeNull();
  });

  it('removes the draft of a deleted signal', async () => {
    writeDraft(draftKey({ name: 'price-drop' }), {
      description: '',
      graph: h0001Graph(),
      layout: {},
    });
    let deleted = false;
    fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json(deleted ? [] : [signal('price-drop')]),
      'DELETE /api/signals/price-drop': () => {
        deleted = true;
        return new Response(null, { status: 204 });
      },
    });
    renderApp('/signals');

    const row = await rowOf('price-drop');
    expect(within(row).getByText('черновик')).toHaveAttribute('data-tone', 'warn');
    await choose(row, 'Удалить');
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Удалить' }),
    );

    expect(await screen.findByText('Сигналов нет.')).toBeInTheDocument();
    expect(readDraft(draftKey({ name: 'price-drop' }))).toBeNull();
  });

  // Scenario «Занятое имя»; spec bench-ui «Меню строки», scenario «Отказ остаётся в диалоге».
  it('shows a taken name on rename in the dialog, which stays open', async () => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json([signal('funding-high'), signal('price-drop')]),
      'POST /api/signals/price-drop/rename': () =>
        json(
          {
            errors: [
              { code: 'NameTaken', reason: 'the name funding-high is taken', field: 'name' },
            ],
          },
          409,
        ),
    });
    renderApp('/signals');

    await choose(await rowOf('price-drop'), 'Переименовать');
    const dialog = screen.getByRole('dialog', { name: 'Переименовать сигнал price-drop' });
    const field = within(dialog).getByRole('textbox', { name: 'Новое имя' });
    expect(field).toHaveValue('price-drop');
    await userEvent.clear(field);
    await userEvent.type(field, 'funding-high');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Переименовать' }));

    expect(await within(dialog).findByText('the name funding-high is taken')).toBeInTheDocument();
    expect(dialog).toBeVisible();
    expect(screen.getAllByRole('row', { name: /^Сигнал / })).toHaveLength(2);
    expect(await rowOf('price-drop')).toBeInTheDocument();
  });

  // Spec bench-ui «Меню строки», scenario «Переименование из меню»; the draft follows the new name.
  it('renames a signal from the menu of its row and moves its draft', async () => {
    writeDraft(draftKey({ name: 'price-drop' }), {
      description: 'правка',
      graph: h0001Graph(),
      layout: {},
    });
    let name = 'price-drop';
    const requests = fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json([signal(name)]),
      'POST /api/signals/price-drop/rename': async (request) => {
        name = ((await request.json()) as { to: string }).to;
        return json({ name, description: null, version: 1, graph: h0001Graph(), layout: {} });
      },
    });
    renderApp('/signals');

    await choose(await rowOf('price-drop'), 'Переименовать');
    const field = screen.getByRole('textbox', { name: 'Новое имя' });
    await userEvent.clear(field);
    await userEvent.type(field, 'price-fall{Enter}');

    expect(await rowOf('price-fall')).toBeInTheDocument();
    expect(screen.queryByRole('row', { name: 'Сигнал price-drop' })).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(await bodiesOf(requests, 'POST', '/api/signals/price-drop/rename')).toEqual([
      { to: 'price-fall' },
    ]);
    expect(readDraft(draftKey({ name: 'price-drop' }))).toBeNull();
    expect(readDraft(draftKey({ name: 'price-fall' }))?.description).toBe('правка');
  });

  it('copies a signal under a new name with its graph', async () => {
    const source = {
      name: 'price-drop',
      description: 'd',
      graph: { formatVersion: 1, nodes: [] },
      layout: {},
    };
    const requests = fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () =>
        json([{ name: 'price-drop', description: 'd', version: 1, error: null }]),
      'GET /api/signals/price-drop': () => json(source),
      'POST /api/signals': () => json({ ...source, name: 'price-drop-copy' }, 201),
    });
    renderApp('/signals');

    await choose(await rowOf('price-drop'), 'Копировать');
    const dialog = screen.getByRole('dialog', { name: 'Копировать сигнал price-drop' });
    expect(within(dialog).getByRole('textbox', { name: 'Имя копии' })).toHaveValue(
      'price-drop-copy',
    );
    await userEvent.click(within(dialog).getByRole('button', { name: 'Скопировать' }));

    await waitFor(async () =>
      expect(await bodiesOf(requests, 'POST', '/api/signals')).toEqual([
        { ...source, name: 'price-drop-copy' },
      ]),
    );
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  // Spec bench-ui «Меню строки».
  it('offers rename, copy and delete in the menu of a row, and only delete for a file that does not read', async () => {
    const requests = fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () =>
        json([signal('broken', { error: 'graph is required', id: null }), signal('price-drop')]),
    });
    renderApp('/signals');

    const row = await rowOf('price-drop');
    expect(within(row).queryByRole('button', { name: 'Удалить' })).toBeNull();
    await userEvent.click(within(row).getByRole('button', { name: 'Действия price-drop' }));
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      'Переименовать',
      'Копировать',
      'Удалить',
    ]);
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();

    const broken = await rowOf('broken');
    await userEvent.click(within(broken).getByRole('button', { name: 'Действия broken' }));
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Удалить']);
    expect(within(broken).queryByRole('button', { name: 'Создать стратегию' })).toBeNull();
    expect(requests.every((request) => request.method === 'GET')).toBe(true);
  });
});

// Spec bench-ui «Поиск в списке».
describe('the search in the list of signals', () => {
  afterEach(() => vi.unstubAllGlobals());

  const listed = () =>
    screen.queryAllByRole('row', { name: /^Сигнал / }).map((row) => row.getAttribute('aria-label'));

  // Scenarios «Часть имени» and «Ничего не найдено».
  it('keeps the rows whose name holds the text, whatever the case, and says when none does', async () => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () => json([signal('candle-drop'), signal('funding-high')]),
    });
    renderApp('/signals');
    await rowOf('funding-high');
    const search = screen.getByRole('searchbox', { name: 'Найти по имени' });

    await userEvent.type(search, 'FUND');
    expect(listed()).toEqual(['Сигнал funding-high']);

    await userEvent.clear(search);
    expect(listed()).toEqual(['Сигнал candle-drop', 'Сигнал funding-high']);

    await userEvent.type(search, 'zzz');
    expect(listed()).toEqual([]);
    expect(screen.getByRole('status')).toHaveTextContent('По запросу zzz ничего не найдено');
    expect(screen.queryByText('Сигналов нет.')).toBeNull();
  });
});

// Spec signal-constructor «Стратегии сигнала в списке».
describe('the strategies of a signal in the list', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenarios «Сигнал со стратегией» and «Сигнал без ссылок».
  it('names the strategies as links to their editors, and puts a dash for a signal without references', async () => {
    fakeApi({
      ...baseRoutes(),
      'GET /api/signals': () =>
        json([
          signal('candle-drop'),
          signal('funding-high', { strategies: ['funding-short', 'funding-short-8h'] }),
        ]),
    });
    renderApp('/signals');

    const used = within(await rowOf('funding-high'));
    expect(used.getByRole('link', { name: 'funding-short' })).toHaveAttribute(
      'href',
      '/strategies/funding-short',
    );
    expect(used.getByRole('link', { name: 'funding-short-8h' })).toHaveAttribute(
      'href',
      '/strategies/funding-short-8h',
    );
    const free = within(await rowOf('candle-drop'));
    expect(free.getByText('—')).toBeInTheDocument();
    expect(free.getAllByRole('link')).toHaveLength(1);
  });
});
