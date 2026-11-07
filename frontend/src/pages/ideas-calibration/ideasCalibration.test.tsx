import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { calibrationOf, calibrationRowOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const asked = (requests: Request[]) =>
  requests
    .filter((request) => new URL(request.url).pathname === '/api/ideas/calibration')
    .map((request) => new URL(request.url).searchParams.toString());

function open(report = calibrationOf()) {
  return fakeApi({ 'GET /api/ideas/calibration': () => json(report) });
}

describe('calibration screen', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the path, the counters and the time of the calculation', async () => {
    open();
    renderApp('/ideas/calibration');

    expect(await screen.findByRole('heading', { name: 'Калибровка' })).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Путь' })).getByRole('link', { name: 'Идеи' }),
    ).toHaveAttribute('href', '/ideas');
    expect(screen.getByRole('link', { name: 'Срок жизни и цель' })).toHaveAttribute(
      'href',
      '/ideas/exit-rules',
    );
    expect(
      await screen.findByText(
        'Проверено 2 412 кейсов · без статистики корзины 318 · посчитано 06.11 03:50',
      ),
    ).toBeInTheDocument();
  });

  // Scenario "Плитка Брайера".
  it('compares the Brier score with the score without buckets in a tile', async () => {
    open();
    renderApp('/ideas/calibration');

    const tile = await screen.findByRole('region', { name: 'Оценка Брайера' });

    expect(within(tile).getByText('0.207 против 0.219')).toBeInTheDocument();
    expect(within(tile).getByText('лучше на 5.5 %')).toBeInTheDocument();
  });

  it('shows the result of the ideas ▲ and the expectancy promised against the one got', async () => {
    open();
    renderApp('/ideas/calibration');

    const up = await screen.findByRole('region', { name: '▲ на деле' });
    const expectancy = screen.getByRole('region', { name: 'Ожидание' });

    expect(within(up).getByText('+0.08 R')).toBeInTheDocument();
    expect(within(up).getByText('+0.02…+0.14 R · 603 кейса')).toBeInTheDocument();
    expect(within(expectancy).getByText('+0.12 R → +0.03 R')).toBeInTheDocument();
  });

  // Scenarios "Высокие вероятности завышены" and "Завышенная корзина".
  it('lists the probability promised against the share of targets, with the difference coloured by the interval', async () => {
    open();
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'Вероятность' });
    const row = within(table).getByRole('row', { name: /≥ 60 %/ });

    expect(
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['≥ 60 %', '296', '64 %', '51 %', '−13 п.п.', '45–57 %', '38 %', '11 %']);
    expect(within(row).getByText('−13 п.п.')).toHaveClass('text-err');
    expect(
      within(within(table).getByRole('row', { name: /30–40 %/ })).getByText('−1 п.п.'),
    ).toHaveClass('text-muted');
  });

  it('lists the expectancy by range and the ideas by badge', async () => {
    open();
    renderApp('/ideas/calibration');

    const expectancies = await screen.findByRole('table', { name: 'Ожидание' });
    const badges = screen.getByRole('table', { name: 'Значки' });

    expect(
      within(within(expectancies).getByRole('row', { name: /0…0.2 R/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['0…0.2 R', '900', '+0.11 R', '+0.04 R']);
    expect(
      within(within(badges).getByRole('row', { name: /▲/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['▲', '603', '25 %', '+0.21 R', '+0.08 R', '+0.02…+0.14 R', '46 %']);
  });

  // Scenario "Переключение окна".
  it('asks for the last 30 days and the category the owner picks, and keeps them in the address', async () => {
    const requests = open();
    const { history } = renderApp('/ideas/calibration');
    await screen.findByRole('region', { name: 'Оценка Брайера' });

    await userEvent.click(screen.getByRole('button', { name: 'за 30 дн' }));
    await waitFor(() => expect(asked(requests)).toContain('Days=30'));
    await userEvent.click(screen.getByRole('button', { name: 'linear' }));

    await waitFor(() => expect(asked(requests)).toContain('Days=30&Category=linear'));
    expect(history.location.search).toBe('?days=30&category=linear');
    expect(screen.getByRole('button', { name: 'за 30 дн' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(asked(requests)[0]).toBe('Days=90');

    await userEvent.click(screen.getByRole('button', { name: 'Кейсы 90 дн' }));
    await userEvent.click(screen.getByRole('button', { name: 'Все' }));

    await waitFor(() => expect(history.location.search).toBe(''));
  });

  // Scenario "Переключение режима".
  it('asks for the cases after the rules changed and keeps the scope in the address', async () => {
    const requests = open();
    const { history } = renderApp('/ideas/calibration');
    await screen.findByRole('region', { name: 'Оценка Брайера' });

    await userEvent.click(screen.getByRole('button', { name: 'После изменения правил' }));

    await waitFor(() => expect(asked(requests)).toContain('Days=90&Scope=fresh'));
    expect(history.location.search).toBe('?scope=fresh');
    expect(screen.getByRole('button', { name: 'После изменения правил' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );

    await userEvent.click(screen.getByRole('button', { name: 'Все случаи' }));

    await waitFor(() => expect(history.location.search).toBe(''));
  });

  it('opens the scope from the address', async () => {
    const requests = open();
    renderApp('/ideas/calibration?scope=fresh');

    await screen.findByRole('region', { name: 'Оценка Брайера' });

    expect(asked(requests)).toEqual(['Days=90&Scope=fresh']);
  });

  // Scenarios "Старая модель против новой" and "Модель с признаками".
  it('compares the old model with the new one, a row each', async () => {
    open();
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'Старая модель → новая' });
    const cells = (name: RegExp) =>
      within(within(table).getByRole('row', { name }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent);

    expect(cells(/Старая/)).toEqual([
      'Старая',
      '1\u00a0800',
      '0.231',
      '1\u00a0200',
      '+0.15 R',
      '-0.07 R',
      '-0.12…-0.03 R',
    ]);
    expect(cells(/Новая/)).toEqual([
      'Новая',
      '1\u00a0800',
      '0.204',
      '300',
      '+0.05 R',
      '+0.03 R',
      '-0.04…+0.10 R',
    ]);
    expect(cells(/С признаками/)).toEqual([
      'С признаками',
      '1\u00a0800',
      '0.201',
      '240',
      '+0.04 R',
      '+0.05 R',
      '-0.02…+0.12 R',
    ]);
  });

  // Scenario "Разница Брайера".
  it('shows the Brier score of the model with features less that of the new one with its interval', async () => {
    open();
    renderApp('/ideas/calibration');

    expect(await screen.findByLabelText('Разница Брайера')).toHaveTextContent(
      'С признаками − новая, Брайер: -0.006, интервал 95 % -0.012…+0.001',
    );
  });

  it('shows the difference without an interval under 30 cases', async () => {
    const report = calibrationOf();
    open(
      calibrationOf({
        comparison: { ...report.comparison!, difference: { mean: '0.01', low: null, high: null } },
      }),
    );
    renderApp('/ideas/calibration');

    expect(await screen.findByLabelText('Разница Брайера')).toHaveTextContent(
      'С признаками − новая, Брайер: +0.010, интервал 95 % — меньше 30 кейсов',
    );
  });

  // Scenario "Отчёт без модели с признаками".
  it('shows the old and the new model only in a report without the model with features', async () => {
    const report = calibrationOf();
    open(
      calibrationOf({ comparison: { ...report.comparison!, features: null, difference: null } }),
    );
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'Старая модель → новая' });

    expect(screen.queryByLabelText('Разница Брайера')).not.toBeInTheDocument();

    expect(within(table).getByRole('row', { name: /Новая/ })).toBeInTheDocument();
    expect(within(table).queryByRole('row', { name: /С признаками/ })).not.toBeInTheDocument();
  });

  it('shows dashes for a model without ▲ and no comparison without one', async () => {
    const report = calibrationOf();
    open(
      calibrationOf({
        comparison: { ...report.comparison!, new: { cases: 1800, brier: '0.204', up: null } },
      }),
    );
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'Старая модель → новая' });

    expect(
      within(within(table).getByRole('row', { name: /Новая/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['Новая', '1\u00a0800', '0.204', '0', '—', '—', '—']);
  });

  // Requirement "Таблицы по ТФ и типам на экране «Калибровка»", scenario "Таблица по ТФ".
  it('lists the cases and the ideas ▲ of each timeframe', async () => {
    open();
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'По ТФ' });

    expect(
      within(within(table).getByRole('row', { name: /1 ч/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual([
      '1 ч',
      '4\u00a0000',
      '+0.02 R',
      '-0.05 R',
      '-0.09…-0.01 R',
      '900',
      '+0.15 R',
      '-0.10 R',
      '-0.18…-0.02 R',
    ]);
  });

  it('shows dashes for a type without ▲ and without an interval', async () => {
    open();
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'По типам' });

    expect(
      within(within(table).getByRole('row', { name: /Откат/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['Откат', '500', '+0.01 R', '+0.02 R', '—', '0', '—', '—', '—']);
  });

  // Scenario "Сравнение по типам".
  it('compares the old model with the new one in each row of a breakdown', async () => {
    open();
    renderApp('/ideas/calibration');

    const types = await screen.findByRole('table', { name: 'По типам: старая модель → новая' });
    const cells = (table: HTMLElement, name: RegExp) =>
      within(within(table).getByRole('row', { name }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent);

    expect(cells(types, /Откат Старая/)).toEqual([
      'Откат',
      'Старая',
      '200',
      '0.240',
      '120',
      '+0.10 R',
      '-0.08 R',
      '-0.16…0.00 R',
      '—',
    ]);
    expect(cells(types, /Откат Новая/)).toEqual([
      'Откат',
      'Новая',
      '200',
      '0.220',
      '40',
      '+0.04 R',
      '+0.01 R',
      '-0.10…+0.12 R',
      '—',
    ]);
    expect(within(types).queryByRole('row', { name: /С признаками/ })).not.toBeInTheDocument();
    const intervals = screen.getByRole('table', { name: 'По ТФ: старая модель → новая' });
    expect(cells(intervals, /1 ч Новая/)).toEqual([
      '1 ч',
      'Новая',
      '1\u00a0500',
      '0.210',
      '0',
      '—',
      '—',
      '—',
      '—',
    ]);
    // Decision 4 of add-idea-features: the slice compares the model with features too, with its difference of the Brier score.
    expect(cells(intervals, /1 ч С признаками/)).toEqual([
      '1 ч',
      'С признаками',
      '1\u00a0500',
      '0.205',
      '0',
      '—',
      '—',
      '—',
      '-0.005, интервал 95 % -0.011…+0.002',
    ]);
  });

  // Requirement "Таблица «По признакам» на экране «Калибровка»", scenario "Таблица по признакам".
  it('lists the cases with and without each feature after the types, with the comparison under it', async () => {
    const report = calibrationOf();
    const [interval] = report.intervals;
    open(
      calibrationOf({
        features: [
          {
            ...interval!,
            interval: null,
            feature: 'day_drop',
            present: true,
            total: calibrationRowOf({
              from: null,
              to: null,
              promised: '-0.2',
              cases: 3000,
              expectancy: '-0.05',
              expectancyLow: '-0.11',
              expectancyHigh: '0.01',
            }),
            up: null,
          },
          { ...interval!, interval: null, feature: 'day_drop', present: false },
        ],
      }),
    );
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'По признакам' });

    expect(
      within(within(table).getByRole('row', { name: /· есть/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual([
      'падение пары за сутки · есть',
      '3 000',
      '-0.20 R',
      '-0.05 R',
      '-0.11…+0.01 R',
      '0',
      '—',
      '—',
      '—',
    ]);
    expect(
      within(table).getByRole('row', { name: /падение пары за сутки · нет/ }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('table', { name: 'По признакам: старая модель → новая' }),
    ).toBeInTheDocument();
    const tables = screen.getAllByRole('table').map((item) => item.getAttribute('aria-label'));
    expect(tables.indexOf('По признакам')).toBeGreaterThan(tables.indexOf('По типам'));
    expect(
      screen.getByText(
        /падение пары за сутки от 5 %, падение BTC за 4 часа от 1 % и funding от 0,05 %/,
      ),
    ).toBeInTheDocument();
  });

  // Scenario "Итог без разбивки по признакам".
  it('shows the tables by timeframe and type without the one by feature in a report stored before it', async () => {
    open(calibrationOf({ features: [] }));
    renderApp('/ideas/calibration');

    await screen.findByRole('table', { name: 'По ТФ' });

    expect(screen.getByRole('table', { name: 'По типам' })).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'По признакам' })).not.toBeInTheDocument();
  });

  // Scenario "Итог без разбивок".
  it('shows no breakdown in a report stored before them', async () => {
    open(calibrationOf({ intervals: [], types: [] }));
    renderApp('/ideas/calibration');

    await screen.findByRole('table', { name: 'Значки' });

    for (const name of [
      'По ТФ',
      'По типам',
      'По ТФ: старая модель → новая',
      'По типам: старая модель → новая',
    ]) {
      expect(screen.queryByRole('table', { name })).not.toBeInTheDocument();
    }
  });

  it('shows no comparison in a report without one', async () => {
    open(calibrationOf({ comparison: null }));
    renderApp('/ideas/calibration');

    await screen.findByRole('region', { name: 'Оценка Брайера' });

    expect(screen.queryByRole('table', { name: 'Старая модель → новая' })).not.toBeInTheDocument();
  });

  // Scenario "Строка «мало своих данных»".
  it('lists the cases by their own data with the Brier score', async () => {
    open();
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'По своим данным' });

    expect(
      within(within(table).getByRole('row', { name: /мало своих данных/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['мало своих данных', '612', '+0.09 R', '+0.01 R', '-0.07…+0.09 R', '38 %', '0.212']);
    expect(within(table).getByRole('row', { name: /30 и больше своих/ })).toBeInTheDocument();
  });

  it('shows a dash for the Brier score of a model without a case', async () => {
    const report = calibrationOf();
    open(
      calibrationOf({
        comparison: { ...report.comparison!, old: { cases: 0, brier: null, up: null } },
      }),
    );
    renderApp('/ideas/calibration');

    const table = await screen.findByRole('table', { name: 'Старая модель → новая' });

    expect(
      within(within(table).getByRole('row', { name: /Старая/ })).getAllByRole('cell')[2],
    ).toHaveTextContent('—');
  });

  it('shows a dash for the intervals of a row under 30 cases and says when no idea had ▲', async () => {
    const few = calibrationRowOf({
      cases: 12,
      probabilityLow: null,
      probabilityHigh: null,
      expectancyLow: null,
      expectancyHigh: null,
    });
    open(
      calibrationOf({
        probabilities: [few],
        badges: [{ ...few, from: null, to: null, badge: 'flat' }],
      }),
    );
    renderApp('/ideas/calibration');

    const probabilities = await screen.findByRole('table', { name: 'Вероятность' });
    const badges = screen.getByRole('table', { name: 'Значки' });
    const up = screen.getByRole('region', { name: '▲ на деле' });

    expect(within(probabilities).getAllByRole('cell')[5]).toHaveTextContent('—');
    expect(within(badges).getAllByRole('cell')[5]).toHaveTextContent('—');
    expect(within(up).getByText('кейсов со значком ▲ нет')).toBeInTheDocument();
  });

  it('says there is no checked case when the buckets had too little history', async () => {
    open(
      calibrationOf({
        checked: 0,
        unchecked: 40,
        brier: null,
        baseBrier: null,
        probabilities: [],
        expectancies: [],
        badges: [],
        total: null,
      }),
    );
    renderApp('/ideas/calibration');

    expect(await screen.findByText('Проверенных кейсов нет.')).toBeInTheDocument();
    expect(screen.getByText(/без статистики корзины 40/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Оценка Брайера' })).not.toBeInTheDocument();
  });

  it('tells when the calibration cannot be loaded', async () => {
    fakeApi({});
    renderApp('/ideas/calibration');

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить калибровку.');
  });

  // Scenario "Ещё не посчитана".
  it('says the calibration is not computed yet before the first time', async () => {
    open(
      calibrationOf({
        computedAt: null,
        checked: 0,
        unchecked: 0,
        brier: null,
        baseBrier: null,
        probabilities: [],
        expectancies: [],
        badges: [],
        total: null,
      }),
    );
    renderApp('/ideas/calibration');

    expect(await screen.findByText(/Калибровка ещё не посчитана/)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});
