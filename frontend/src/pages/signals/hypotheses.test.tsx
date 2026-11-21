import { screen, within } from '@testing-library/react';

import { fakeApi, json } from '@/test/fakeApi';
import { h0001 } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';
import { baseRoutes, runsOf } from '@/test/routes';

const queued = {
  id: 'r7',
  hypothesis: 'H0001',
  version: 1,
  period: 'exploration',
  kind: 'verdict',
  state: 'queued',
  queuedAt: '2026-11-21T00:10:00Z',
  startedAt: null,
  finishedAt: null,
  worker: null,
  pairsDone: 0,
  pairsTotal: null,
  lostLeases: 0,
  cancelRequested: false,
  result: null,
};

const finished = {
  ...queued,
  id: 'r6',
  state: 'finished',
  queuedAt: null,
  finishedAt: '2026-11-21T00:01:00Z',
  result: {
    time: '2026-11-21T00:01:00Z',
    status: 'insufficient_data',
    reason: 'power 0.13 < 0.8 for effect 0.003',
    hash: 'h',
    version: 1,
    engineVersion: 1,
    power: 0.13,
  },
};

/** The list of hypotheses with H0001 and its last run; without one the field is not sent, as the API leaves out nulls. */
function hypotheses(lastRun?: object) {
  return {
    ...baseRoutes(),
    'GET /api/hypotheses': () =>
      json([
        {
          id: 'H0001',
          version: 1,
          name: 'funding-short-4h',
          timeframe: '4h',
          signal: h0001.signal,
          error: null,
          runs: runsOf(),
          ...(lastRun ? { lastRun } : {}),
        },
      ]),
  };
}

// Spec bench-run-queue «Последний прогон гипотезы».
describe('the last run of a hypothesis in the table of hypotheses', () => {
  afterEach(() => vi.unstubAllGlobals());

  const row = async () => within(await screen.findByRole('rowgroup', { name: 'Гипотеза H0001' }));

  // Scenario «Законченный прогон»; spec bench-ui «Цвет статуса прогона», scenario «Один статус на двух страницах».
  it('shows the status, the period and the kind of a finished run and leads to its page', async () => {
    fakeApi(hypotheses(finished));
    renderApp('/signals');

    const hypothesis = await row();
    expect(hypothesis.getByText('Данных мало')).toHaveAttribute('data-tone', 'warn');
    const link = hypothesis.getByRole('link', { name: /Данных мало/ });
    expect(link).toHaveAttribute('href', '/runs/r6');
    expect(link).toHaveTextContent('разведка');
    expect(link).toHaveTextContent('с вердиктом');
    expect(link).toHaveTextContent('21.11 00:01');
  });

  // Scenario «Прогон в очереди важнее законченного».
  it('shows a run that waits as «В очереди»', async () => {
    fakeApi(hypotheses(queued));
    renderApp('/signals');

    const hypothesis = await row();
    expect(hypothesis.getByText('В очереди')).toHaveAttribute('data-tone', 'muted');
    expect(hypothesis.getByRole('link', { name: /В очереди/ })).toHaveAttribute('href', '/runs/r7');
  });

  // Scenario «Новая версия без прогонов».
  it('says «не запускалась» for a version without runs', async () => {
    fakeApi(hypotheses());
    renderApp('/signals');

    const hypothesis = await row();
    expect(hypothesis.getByText('не запускалась')).toBeInTheDocument();
    expect(hypothesis.queryByRole('link', { name: /прогон/i })).toBeNull();
  });

  it('lays the hypothesis out in the columns of the table', async () => {
    fakeApi(hypotheses());
    renderApp('/signals');

    const hypothesis = await row();
    const table = within(screen.getByRole('region', { name: 'Гипотезы' }));
    expect(table.getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      'ID',
      'Имя',
      'ТФ',
      'Версия',
      'Последний прогон',
      'Запуск',
    ]);
    expect(hypothesis.getByText('funding-short-4h')).toBeInTheDocument();
    expect(hypothesis.getByText('версия 1')).toBeInTheDocument();
    expect(hypothesis.getByText('4h')).toBeInTheDocument();
  });
});
