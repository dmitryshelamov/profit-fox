import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { exitOutOfSampleOf, exitPreviewOf, exitRuleOf, exitRulesOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const previews = (requests: Request[]) =>
  requests
    .filter((request) => new URL(request.url).pathname === '/api/ideas/exit-rules/preview')
    .map((request) => new URL(request.url).searchParams.toString());

const checks = (requests: Request[]) =>
  requests
    .filter((request) => new URL(request.url).pathname === '/api/ideas/exit-rules/out-of-sample')
    .map((request) => new URL(request.url).searchParams.toString());

function open(
  rules = exitRulesOf(),
  preview = exitPreviewOf(),
  check: (request: Request) => Response | Promise<Response> = () => json(exitOutOfSampleOf()),
) {
  return fakeApi({
    'GET /api/ideas/exit-rules': () => json(rules),
    'GET /api/ideas/exit-rules/preview': () => json(preview),
    'GET /api/ideas/exit-rules/out-of-sample': check,
  });
}

const cells = (table: HTMLElement, row: RegExp) =>
  within(within(table).getByRole('row', { name: row }))
    .getAllByRole('cell')
    .map((cell) => cell.textContent);

describe('screen of the exit rules', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Открытие экрана".
  it('shows the path and the rule in force of the type and interval chosen', async () => {
    open(
      exitRulesOf([
        exitRuleOf({
          type: 'target_2r',
          interval: '60',
          life: 48,
          window: null,
          appliedAt: '2026-11-06T10:15:00Z',
        }),
      ]),
    );
    renderApp('/ideas/exit-rules');

    expect(await screen.findByRole('heading', { name: 'Срок жизни и цель' })).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Путь' })).getByRole('link', { name: 'Идеи' }),
    ).toHaveAttribute('href', '/ideas');
    await userEvent.click(screen.getByRole('button', { name: 'Откат' }));
    await userEvent.click(screen.getByRole('button', { name: '15 мин' }));
    expect(
      await screen.findByText('срок 24 свечи · цель 2R · окно входа 4 свечи'),
    ).toBeInTheDocument();
    expect(screen.getByText(/по умолчанию/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Цель 2R' }));
    await userEvent.click(screen.getByRole('button', { name: '1 ч' }));
    expect(await screen.findByText('срок 48 свечей · цель 2R')).toBeInTheDocument();
    expect(screen.getByText(/применено 06\.11 10:15 UTC/)).toBeInTheDocument();
  });

  // Scenario "Уровни без цели".
  it('has only the lifetime for «Уровни»', async () => {
    open();
    renderApp('/ideas/exit-rules');

    await userEvent.click(await screen.findByRole('button', { name: 'Уровни' }));

    expect(await screen.findByRole('combobox', { name: 'Срок жизни, свечей' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Цель, R' })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Окно входа, свечей' })).not.toBeInTheDocument();
  });

  // Scenario "Ввод времени": 10 hours on 15 minutes are 48 candles, and the screen says how it rounded.
  it('rounds a time up to the grid and asks the preview of it', async () => {
    const requests = open();
    renderApp('/ideas/exit-rules');

    const hours = await screen.findByRole('textbox', { name: 'Срок жизни, часов' });
    await userEvent.type(hours, '10{Enter}');

    expect(screen.getByRole('combobox', { name: 'Срок жизни, свечей' })).toHaveValue('48');
    expect(screen.getByText('введено 10 ч — округлено до 48 свечей')).toBeInTheDocument();
    await waitFor(() =>
      expect(previews(requests)).toContain('type=pullback&interval=15&life=48&target=2&window=4'),
    );
  });

  // Scenario "Подгонка".
  it('compares the rule now and the candidate over 90 and 30 days, and warns of a fit', async () => {
    open();
    renderApp('/ideas/exit-rules');

    const table = await screen.findByRole('table', { name: 'Все 90 дней' });

    expect(
      within(within(table).getByRole('row', { name: /Ожидание на вход/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['≈ -0.06 R', '≈ -0.02 R']);
    expect(
      within(within(table).getByRole('row', { name: /Вход исполнен/ }))
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['50 %', '50 %']);
    expect(
      within(within(table).getByRole('row', { name: /Из входов/ })).getAllByRole('cell')[0],
    ).toHaveTextContent('30 % · 60 % · 10 %');
    expect(screen.getByRole('status')).toHaveTextContent(
      'на последних 30 днях почти не лучше — похоже на подгонку',
    );
  });

  // Penpot board «Идеи — срок жизни и цель»: the rules compared over the columns, what the comparison is made on, and the rule of the charts.
  it('names the rule now and the candidate over the columns and the charts', async () => {
    open();
    renderApp('/ideas/exit-rules');

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Цель, R' }), '2.5');
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Срок жизни, свечей' }),
      '48',
    );

    // The columns name the rule the preview was asked for, 300 ms after the last change.
    const table = await screen.findByRole('table', { name: 'Все 90 дней' });
    await within(table).findByRole('columnheader', { name: 'станет · 48 · 2.5R · 4' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['', 'было · 24 · 2R · 4', 'станет · 48 · 2.5R · 4']);
    expect(screen.getByRole('heading', { name: 'Было → станет' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'по прошлым сигналам «Откат» 15 мин всех сетапов и пар · после комиссий и проскальзывания',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText('- - нужно, чтобы цель x R окупилась после комиссий и проскальзывания'),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Ожидание от срока жизни' }).nextElementSibling,
    ).toHaveTextContent('при цели 2.5R и окне входа 4 свечи · все 90 дней');
    expect(
      screen.getByRole('heading', { name: 'Кривая MFE: как далеко цена уходит в нашу сторону' })
        .nextElementSibling,
    ).toHaveTextContent(
      'доля входов, дошедших до +x R раньше стопа за срок 48 свечей · все 90 дней',
    );
  });

  // The lifetimes are signed at the first one and at a round time, the levels of MFE at the whole R and the first.
  it('signs the axes of the charts sparsely', async () => {
    open();
    renderApp('/ideas/exit-rules');

    const lives = await screen.findByRole('img', { name: 'Ожидание от срока' });
    const mfe = screen.getByRole('img', { name: 'Кривая MFE' });
    const ticks = (chart: HTMLElement) =>
      [...chart.querySelectorAll('[data-tick]')].map((tick) => tick.textContent);

    expect(ticks(lives)).toEqual(['1', '4 · 1 ч', '16 · 4 ч', '96 · 1 дн', '672 · 7 дн']);
    expect(ticks(mfe)).toEqual(['+0.5 R', '+1 R', '+2 R', '+3 R', '+4 R', '+5 R']);
  });

  // Scenario "Отметка выбранной цели".
  it('marks the target chosen on the curve of MFE with its share and its break-even', async () => {
    open();
    renderApp('/ideas/exit-rules');

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Цель, R' }), '2.5');

    expect(await screen.findByText('цель 2.5R: дошли 29 %, нужно 30 %')).toBeInTheDocument();
  });

  // Scenario "Применение с экрана".
  it('applies the candidate, shows the rule applied and leaves nothing to apply', async () => {
    const applied = exitRuleOf({ life: 48, appliedAt: '2026-11-06T12:00:00Z' });
    let rules = exitRulesOf();
    const requests = fakeApi({
      'GET /api/ideas/exit-rules': () => json(rules),
      'GET /api/ideas/exit-rules/preview': () => json(exitPreviewOf()),
      'PUT /api/ideas/exit-rules/pullback/15': () => {
        rules = exitRulesOf([applied]);

        return json(applied);
      },
    });
    renderApp('/ideas/exit-rules');

    const apply = await screen.findByRole('button', { name: 'Применить' });
    expect(apply).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Сбросить' })).toBeDisabled();
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Срок жизни, свечей' }),
      '48',
    );
    await userEvent.click(apply);

    expect(
      await screen.findByText('срок 48 свечей · цель 2R · окно входа 4 свечи'),
    ).toBeInTheDocument();
    expect(screen.getByText(/применено 06\.11 12:00 UTC/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Применить' })).toBeDisabled();
    const put = requests.find((request) => request.method === 'PUT')!;
    expect(await put.json()).toEqual({ life: 48, target: 2, window: 4 });
  });

  it('puts the fields back to the rule in force', async () => {
    open();
    renderApp('/ideas/exit-rules');

    const life = await screen.findByRole('combobox', { name: 'Срок жизни, свечей' });
    await userEvent.selectOptions(life, '96');
    await userEvent.click(screen.getByRole('button', { name: 'Сбросить' }));

    expect(life).toHaveValue('24');
  });

  // Scenario "Проверка на экране" of check-idea-exit-rules-out-of-sample: the candidate and the best rule of the selection on both parts,
  // asked for the candidate as the preview is, and the overfitting with its warning.
  it('checks the candidate and the best rule of the grid on the days they were not chosen on', async () => {
    const requests = open();
    renderApp('/ideas/exit-rules');

    const table = await screen.findByRole('table', { name: 'Проверка вне выборки' });

    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['', 'подбор: с 08.08 12:00', 'проверка: 07.10 12:00 – 30.10 11:59']);
    expect(within(table).getByRole('row', { name: /кандидат/ })).toHaveTextContent(
      'кандидат · 24 · 2R · 4 · подбор до 07.10 05:59',
    );
    expect(cells(table, /кандидат/)).toEqual([
      '≈ +0.05 R · от -0.02 до +0.12 · 60 входов',
      '≈ -0.03 R · от -0.10 до +0.04 · 40 входов',
    ]);
    expect(within(table).getByRole('row', { name: /лучшее на подборе/ })).toHaveTextContent(
      'лучшее на подборе · 96 · 3R · 4 · подбор до 06.10 11:59',
    );
    expect(cells(table, /лучшее на подборе/)).toEqual([
      '▲ +0.12 R · от +0.02 до +0.22 · 60 входов',
      '-0.01 R · мало данных · 12 входов',
    ]);
    expect(screen.getByText(/сетка — 171 правило/)).toBeInTheDocument();
    expect(
      screen.getByText('Вероятность подгонки сетки: 65 % (13 из 20 разбиений)'),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'лучшее на одной половине дней правило сетки чаще не лучше середины на другой — выбор по ней ненадёжен',
      ),
    ).toBeInTheDocument();
    expect(checks(requests)).toContain('type=pullback&interval=15&life=24&target=2&window=4');
  });

  // Scenario "Нет лучшего правила".
  it('says when the selection has no rule of 30 entries and the splits too little data', async () => {
    open(exitRulesOf(), exitPreviewOf(), () =>
      json(
        exitOutOfSampleOf({
          candidate: { selectionTo: '2026-10-07T05:59:00Z', selection: null, check: null },
          best: null,
          overfitting: null,
        }),
      ),
    );
    renderApp('/ideas/exit-rules');

    const table = await screen.findByRole('table', { name: 'Проверка вне выборки' });

    expect(cells(table, /кандидат/)).toEqual(['нет входов', 'нет входов']);
    expect(within(table).getByText('на подборе нет правила с 30 входами')).toBeInTheDocument();
    expect(
      screen.getByText('Вероятность подгонки сетки: мало данных для разбиений'),
    ).toBeInTheDocument();
  });

  // The check comes apart from the preview: while it is counted the rest of the screen is there.
  it('shows the preview while the check is counted', async () => {
    open(exitRulesOf(), exitPreviewOf(), () => new Promise<Response>(() => {}));
    renderApp('/ideas/exit-rules');

    expect(await screen.findByRole('table', { name: 'Все 90 дней' })).toBeInTheDocument();
    expect(screen.getByText('Проверка считается…')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Проверка вне выборки' })).not.toBeInTheDocument();
  });

  it('says when the check failed and keeps the preview', async () => {
    open(exitRulesOf(), exitPreviewOf(), () => problem(400, 'window'));
    renderApp('/ideas/exit-rules');

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Не удалось проверить кандидата вне выборки.',
    );
    expect(screen.getByRole('table', { name: 'Все 90 дней' })).toBeInTheDocument();
  });

  // Finding 4 of the review: a check asked for a candidate the owner moved past is cancelled.
  it('cancels the check of a candidate the owner moved past', async () => {
    const signals: AbortSignal[] = [];
    open(exitRulesOf(), exitPreviewOf(), (request) => {
      signals.push(request.signal);

      return new Promise<Response>(() => {});
    });
    renderApp('/ideas/exit-rules');

    await waitFor(() => expect(signals).toHaveLength(1));
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Срок жизни, свечей' }),
      '48',
    );

    await waitFor(() => expect(signals).toHaveLength(2));
    expect(signals.map((signal) => signal.aborted)).toEqual([true, false]);
  });

  // Finding 5 of the review: the numbers of the candidate before never stand under the new one, nor beside a failure.
  it('hides the check of the candidate before while the new one is counted or failed', async () => {
    let answer: () => Response | Promise<Response> = () => json(exitOutOfSampleOf());
    open(exitRulesOf(), exitPreviewOf(), () => answer());
    renderApp('/ideas/exit-rules');

    await screen.findByRole('table', { name: 'Проверка вне выборки' });
    answer = () => new Promise<Response>(() => {});
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Срок жизни, свечей' }),
      '48',
    );

    expect(await screen.findByText('Проверка считается…')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Проверка вне выборки' })).not.toBeInTheDocument();

    answer = () => problem(400, 'life');
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Срок жизни, свечей' }),
      '96',
    );

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Не удалось проверить кандидата вне выборки.',
    );
    expect(screen.queryByRole('table', { name: 'Проверка вне выборки' })).not.toBeInTheDocument();
  });
});
