import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, type FakeRoute, json, problem } from '@/test/fakeApi';
import { filterFields, marketRow, marketSnapshot, savedFilterOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const hot = marketRow({
  symbol: 'HOTUSDT',
  change1h: '4.2',
  fundingRate: '0.0005',
  turnover24h: '80000000',
});
const warm = marketRow({
  symbol: 'WARMUSDT',
  change1h: '4.2',
  fundingRate: '0.0001',
  turnover24h: '10000000',
});
const cold = marketRow({
  symbol: 'COLDUSDT',
  change1h: '-1',
  fundingRate: '0.0006',
  turnover24h: '90000000',
});

const shorting = savedFilterOf();
const onIdeas = savedFilterOf({
  id: '33333333-3333-4333-8333-333333333333',
  name: 'Только идеи',
  place: 'ideas',
});
const both = savedFilterOf({
  id: '44444444-4444-4444-8444-444444444444',
  name: 'Крупные',
  place: 'both',
  conditions: [
    { field: 'turnover24h', operator: '>', value: '50000000', to: null },
    { field: 'probability', operator: '>', value: '0.5', to: null },
  ],
});

const lastRead = (requests: Request[]) =>
  Object.fromEntries(
    new URL(
      requests.filter((request) => new URL(request.url).pathname === '/api/market').at(-1)!.url,
    ).searchParams,
  );

// The API counts what fits: 3 pairs and 2 ideas, and a condition on the turnover leaves 2 pairs.
const fitting = async (request: Request) => {
  const body = (await request.json()) as { conditions: { field: string }[] };

  return json({
    pairs: body.conditions.some((condition) => condition.field === 'turnover24h') ? 2 : 3,
    ideas: body.conditions.some((condition) => condition.field === 'probability') ? 0 : 2,
  });
};

function open(filters = [shorting, onIdeas, both], extra: Record<string, FakeRoute> = {}) {
  return fakeApi({
    'GET /api/market': () => json(marketSnapshot([hot, warm, cold])),
    'GET /api/saved-filters': () => json(filters),
    'GET /api/saved-filters/fields': () => json(filterFields),
    'POST /api/saved-filters/fit': fitting,
    ...extra,
  });
}

describe('saved filters on the market screen', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the filters of the market and of both places, not the ones of the ideas', async () => {
    open();
    renderApp('/market');

    const row = await screen.findByRole('group', { name: 'Фильтры' });

    await waitFor(() =>
      expect(
        within(row)
          .getAllByRole('button')
          .map((button) => button.textContent),
      ).toEqual(['Шорт перегретых', 'Крупные', '+ Новый фильтр']),
    );
  });

  // Scenario "Выбор фильтра на рынке": the API applies the filter, the screen names it and writes its conditions under the chips.
  it('asks for the pairs that meet the filter, counts them and writes the conditions under the chips', async () => {
    const requests = open();
    renderApp('/market');
    await screen.findByRole('table');

    await userEvent.click(await screen.findByRole('button', { name: 'Шорт перегретых' }));

    await waitFor(() => expect(lastRead(requests)).toMatchObject({ SavedFilter: shorting.id }));
    expect(screen.getByText('3 из 3 инструментов')).toBeInTheDocument();
    expect(screen.getByLabelText('Условия фильтра')).toHaveTextContent(
      'Изменение 1ч > 3 % и Фандинг > 0.03 %',
    );
  });

  it('takes the filter off with «Снять» and brings the pairs back', async () => {
    open(undefined, {
      'GET /api/market': (request) =>
        json(
          new URL(request.url).searchParams.has('SavedFilter')
            ? marketSnapshot([hot], { pairs: 3 })
            : marketSnapshot([hot, warm, cold]),
        ),
    });
    renderApp('/market');
    await screen.findByRole('table');
    await userEvent.click(await screen.findByRole('button', { name: 'Шорт перегретых' }));
    expect(await screen.findByText('1 из 3 инструментов')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Снять' }));

    expect(await screen.findByText('3 из 3 инструментов')).toBeInTheDocument();
    expect(screen.queryByLabelText('Условия фильтра')).not.toBeInTheDocument();
  });

  it('asks for the filter together with the setup chip', async () => {
    const requests = open();
    renderApp('/market');
    await screen.findByRole('table');
    await userEvent.click(await screen.findByRole('button', { name: 'Шорт перегретых' }));

    await userEvent.click(screen.getByRole('button', { name: 'Импульс 0' }));

    await waitFor(() =>
      expect(lastRead(requests)).toMatchObject({ SavedFilter: shorting.id, Setup: 'impulse' }),
    );
  });

  describe('editor', () => {
    const editor = () => within(screen.getByRole('dialog', { name: 'Редактор фильтра' }));

    /** Opens the picker of the last condition and takes a field of it. */
    async function pickField(label: string, column: 'Рынок' | 'Идея' = 'Рынок') {
      const pickers = editor().getAllByRole('button', { name: 'Поле' });

      await userEvent.click(pickers.at(-1)!);
      await userEvent.click(
        within(editor().getByRole('group', { name: column })).getByRole('menuitemradio', {
          name: label,
        }),
      );
    }

    async function openNew() {
      await screen.findByRole('table');
      await userEvent.click(await screen.findByRole('button', { name: '+ Новый фильтр' }));
    }

    const fits = (requests: Request[]) =>
      requests.filter((request) => new URL(request.url).pathname === '/api/saved-filters/fit');
    const lastFit = async (requests: Request[]) =>
      (await fits(requests).at(-1)!.clone().json()) as { conditions: unknown[] };

    // Scenario "Живой счётчик": the API counts, after a pause, by the conditions that are ready.
    it('recounts what fits while a condition is edited, without saving', async () => {
      const requests = open();
      renderApp('/market');
      await openNew();

      expect(await editor().findByText('Подходит: 3 инструмента · 2 идеи')).toBeInTheDocument();
      await userEvent.click(editor().getByRole('button', { name: '+ Условие' }));
      await pickField('Оборот 24ч');
      await userEvent.type(editor().getByRole('textbox', { name: 'Значение' }), '50000000');

      expect(await editor().findByText('Подходит: 2 инструмента · 2 идеи')).toBeInTheDocument();
      expect(await lastFit(requests)).toEqual({
        conditions: [{ field: 'turnover24h', operator: '>', value: '50000000', to: null }],
      });
      expect(
        requests.filter(
          (request) =>
            request.method === 'POST' && new URL(request.url).pathname === '/api/saved-filters',
        ),
      ).toHaveLength(0);
    });

    it('asks once for the conditions typed, after the pause, and not for one that is not a number yet', async () => {
      const requests = open();
      renderApp('/market');
      await openNew();
      await editor().findByText('Подходит: 3 инструмента · 2 идеи');
      await userEvent.click(editor().getByRole('button', { name: '+ Условие' }));
      await pickField('Оборот 24ч');
      const before = fits(requests).length;

      await userEvent.type(editor().getByRole('textbox', { name: 'Значение' }), '5e');

      expect(fits(requests)).toHaveLength(before);
      await userEvent.type(editor().getByRole('textbox', { name: 'Значение' }), '0');
      await editor().findByText('Подходит: 3 инструмента · 2 идеи');
    });

    it('counts the ideas by the idea fields', async () => {
      const requests = open();
      renderApp('/market');
      await openNew();

      await userEvent.click(editor().getByRole('button', { name: '+ Условие' }));
      await pickField('Вероятность', 'Идея');
      await userEvent.type(editor().getByRole('textbox', { name: 'Значение' }), '50');

      // The idea fields are marked «Идеи»; the probability is typed in percent: 50 is 0.5.
      expect(editor().getByText('Идеи', { selector: 'span' })).toBeInTheDocument();
      expect(await editor().findByText('Подходит: 3 инструмента · 0 идей')).toBeInTheDocument();
      expect(await lastFit(requests)).toEqual({
        conditions: [{ field: 'probability', operator: '>', value: '0.5', to: null }],
      });
    });

    it('saves the filter with funding as a fraction and shows it as chosen', async () => {
      let saved = [shorting];
      let posted: unknown = null;
      open(undefined, {
        'GET /api/saved-filters': () => json(saved),
        'POST /api/saved-filters': async (request) => {
          posted = await request.json();
          const created = savedFilterOf({
            id: 'new',
            name: 'Фандинг',
            conditions: [{ field: 'fundingRate', operator: '>', value: '0.0003', to: null }],
          });
          saved = [shorting, created];

          return json(created, 201);
        },
      });
      renderApp('/market');
      await openNew();

      await userEvent.type(editor().getByRole('textbox', { name: 'Название' }), 'Фандинг');
      await userEvent.click(editor().getByRole('button', { name: '+ Условие' }));
      await pickField('Фандинг');
      await userEvent.type(editor().getByRole('textbox', { name: 'Значение' }), '0.03');
      await userEvent.click(editor().getByRole('button', { name: 'Сохранить' }));

      await waitFor(() =>
        expect(screen.queryByRole('dialog', { name: 'Редактор фильтра' })).not.toBeInTheDocument(),
      );
      expect(posted).toEqual({
        name: 'Фандинг',
        place: 'market',
        conditions: [{ field: 'fundingRate', operator: '>', value: '0.0003', to: null }],
      });
      expect(await screen.findByLabelText('Условия фильтра')).toHaveTextContent('Фандинг > 0.03 %');
      expect(screen.getByRole('button', { name: 'Фандинг' })).toHaveAttribute(
        'aria-pressed',
        'true',
      );
    });

    // Scenario "Фильтр без условий".
    it('shows the errors of the server in Russian and keeps the editor open', async () => {
      open(undefined, {
        'POST /api/saved-filters': () =>
          problem(400, 'Validation failed', {
            errors: {
              name: ['Name is required.'],
              conditions: ['A filter needs at least one condition.'],
            },
          }),
      });
      renderApp('/market');
      await openNew();

      await userEvent.click(editor().getByRole('button', { name: 'Сохранить' }));

      expect(await screen.findByText('Укажите имя фильтра.')).toBeInTheDocument();
      expect(screen.getByText('Добавьте хотя бы одно условие.')).toBeInTheDocument();
      expect(screen.getByRole('dialog', { name: 'Редактор фильтра' })).toBeInTheDocument();
    });

    it('opens a chosen filter with «Изменить», saves it with PUT and deletes it', async () => {
      const requests = open(undefined, {
        'PUT /api/saved-filters/22222222-2222-4222-8222-222222222222': () =>
          new Response(null, { status: 204 }),
        'DELETE /api/saved-filters/22222222-2222-4222-8222-222222222222': () =>
          new Response(null, { status: 204 }),
      });
      renderApp('/market');
      await screen.findByRole('table');
      await userEvent.click(await screen.findByRole('button', { name: 'Шорт перегретых' }));

      await userEvent.click(screen.getByRole('button', { name: 'Изменить' }));

      expect(editor().getByRole('textbox', { name: 'Название' })).toHaveValue('Шорт перегретых');
      // Funding is typed in percent: 0.0003 is 0.03.
      expect(
        editor()
          .getAllByRole('textbox', { name: 'Значение' })
          .map((input) => (input as HTMLInputElement).value),
      ).toEqual(['3', '0.03']);
      await userEvent.click(editor().getByRole('button', { name: 'Сохранить' }));
      await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
      // The API answers a change with no body: the filter stays chosen.
      expect(await screen.findByLabelText('Условия фильтра')).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Изменить' }));
      await userEvent.click(editor().getByRole('button', { name: 'Удалить фильтр' }));

      await waitFor(() =>
        expect(requests.some((request) => request.method === 'DELETE')).toBe(true),
      );
      await waitFor(() =>
        expect(screen.queryByLabelText('Условия фильтра')).not.toBeInTheDocument(),
      );
    });

    it('keeps «Сохранить» off while a value is not a number', async () => {
      open();
      renderApp('/market');
      await openNew();
      await userEvent.type(editor().getByRole('textbox', { name: 'Название' }), 'Ф');
      await userEvent.click(editor().getByRole('button', { name: '+ Условие' }));
      await pickField('Изменение 5м');

      await userEvent.type(editor().getByRole('textbox', { name: 'Значение' }), 'много');

      expect(editor().getByRole('button', { name: 'Сохранить' })).toBeDisabled();
      await userEvent.clear(editor().getByRole('textbox', { name: 'Значение' }));
      await userEvent.type(editor().getByRole('textbox', { name: 'Значение' }), '3,5');
      expect(editor().getByRole('button', { name: 'Сохранить' })).toBeEnabled();
    });

    it('lists the fields of the market and of the idea in two columns, in the order of the catalog', async () => {
      open();
      renderApp('/market');
      await openNew();
      await userEvent.click(editor().getByRole('button', { name: '+ Условие' }));

      await userEvent.click(editor().getByRole('button', { name: 'Поле' }));

      const names = (column: string) =>
        within(editor().getByRole('group', { name: column }))
          .getAllByRole('menuitemradio')
          .map((item) => item.textContent);

      expect(names('Рынок')).toEqual([
        'Категория',
        'Изменение 5м',
        'Изменение 15м',
        'Изменение 1ч',
        'Изменение 4ч',
        'Изменение 24ч',
        'Оборот 1ч',
        'Оборот 24ч',
        'OI',
        'ΔOI 1ч',
        'ΔOI 24ч',
        'Фандинг',
        'Спред',
        'Сетап',
      ]);
      expect(names('Идея')).toEqual([
        'Сетап',
        'Тип идеи',
        'ТФ',
        'Сторона',
        'R:R',
        'Вероятность',
        'Ожидание',
      ]);
    });

    it('takes a choice field as a select of named choices and stores the code', async () => {
      let posted: unknown = null;
      open(undefined, {
        'POST /api/saved-filters': async (request) => {
          posted = await request.json();

          return json(savedFilterOf({ id: 'new' }), 201);
        },
      });
      renderApp('/market');
      await openNew();
      await userEvent.type(editor().getByRole('textbox', { name: 'Название' }), 'Шорты');
      await userEvent.click(editor().getByRole('button', { name: 'Идеи' }));
      await userEvent.click(editor().getByRole('button', { name: '+ Условие' }));
      await pickField('Сторона', 'Идея');

      expect(editor().getByRole('combobox', { name: 'Оператор' })).toHaveValue('=');
      expect(
        within(editor().getByRole('combobox', { name: 'Значение' }))
          .getAllByRole('option')
          .map((option) => option.textContent),
      ).toEqual(['Лонг', 'Шорт']);
      expect(editor().getByText('Идеи', { selector: 'span' })).toBeInTheDocument();
      await userEvent.selectOptions(editor().getByRole('combobox', { name: 'Значение' }), 'Шорт');
      await userEvent.click(editor().getByRole('button', { name: 'Сохранить' }));

      await waitFor(() =>
        expect(posted).toEqual({
          name: 'Шорты',
          place: 'ideas',
          conditions: [{ field: 'side', operator: '=', value: 'short', to: null }],
        }),
      );
    });

    it('offers «выше базовой» as an operator of the probability and needs no value for it', async () => {
      open();
      renderApp('/market');
      await openNew();
      await userEvent.click(editor().getByRole('button', { name: '+ Условие' }));
      await pickField('Вероятность', 'Идея');

      await userEvent.selectOptions(
        editor().getByRole('combobox', { name: 'Оператор' }),
        'above_baseline',
      );

      expect(editor().queryByRole('textbox', { name: 'Значение' })).not.toBeInTheDocument();
      expect(await editor().findByText('Подходит: 3 инструмента · 2 идеи')).toBeInTheDocument();
    });

    it('has the header, the note about the ideas and no «Удалить фильтр» for a new filter', async () => {
      open();
      renderApp('/market');
      await openNew();

      expect(editor().getByRole('heading', { name: 'Фильтр' })).toBeInTheDocument();
      expect(editor().getByText('Условия · соединяются через «и»')).toBeInTheDocument();
      expect(
        editor().getByText('Условия по идеям действуют только в «Идеях»; в «Рынке» их нет.'),
      ).toBeInTheDocument();
      expect(editor().getByRole('button', { name: 'Рынок' })).toHaveAttribute(
        'aria-pressed',
        'true',
      );
      expect(editor().queryByRole('button', { name: 'Удалить фильтр' })).not.toBeInTheDocument();
    });

    it('closes without saving on «Отмена»', async () => {
      open();
      renderApp('/market');
      await openNew();

      await userEvent.click(editor().getByRole('button', { name: 'Отмена' }));

      expect(screen.queryByRole('dialog', { name: 'Редактор фильтра' })).not.toBeInTheDocument();
    });

    it('closes without saving on the cross', async () => {
      open();
      renderApp('/market');
      await openNew();

      await userEvent.click(editor().getByRole('button', { name: 'Закрыть' }));

      expect(screen.queryByRole('dialog', { name: 'Редактор фильтра' })).not.toBeInTheDocument();
    });
  });
});
