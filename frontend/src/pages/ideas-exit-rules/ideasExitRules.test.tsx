import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { exitPreviewOf, exitRuleOf, exitRulesOf } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const previews = (requests: Request[]) =>
  requests
    .filter((request) => new URL(request.url).pathname === '/api/ideas/exit-rules/preview')
    .map((request) => new URL(request.url).searchParams.toString());

function open(rules = exitRulesOf(), preview = exitPreviewOf()) {
  return fakeApi({
    'GET /api/ideas/exit-rules': () => json(rules),
    'GET /api/ideas/exit-rules/preview': () => json(preview),
  });
}

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
});
