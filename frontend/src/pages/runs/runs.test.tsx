import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';

import { Markdown } from './Markdown';

const base = {
  hypothesis: 'H0001',
  version: 1,
  period: 'exploration',
  kind: 'verdict',
  queuedAt: '2026-11-20T08:00:00Z',
  startedAt: null,
  finishedAt: null,
  worker: null,
  pairsDone: 0,
  pairsTotal: null,
  lostLeases: 0,
  cancelRequested: false,
  result: null,
};

const line = {
  time: '2026-11-20T09:00:00Z',
  status: 'insufficient_data',
  reason: 'power 0.74 is below 0.8',
  hash: 'h',
  version: 1,
  engineVersion: 2,
  dataHash: 'aa9c7db8',
  attempts: 1,
  requiredT: 3,
  power: 0.74,
  blocks: 26,
  events: 3259,
  t: null,
  mean: null,
  firstHalfMean: null,
  secondHalfMean: null,
  readSeconds: 8764,
  corrects: null,
  report: null,
};

describe('the section «Прогоны»', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario «Прогон закончился, пока страница открыта».
  it('moves a run that ended while the page is open to the finished ones', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let ended = false;
    fakeApi({
      'GET /api/runs': (request) => {
        const active = new URL(request.url).searchParams.get('active') === 'true';
        if (active) {
          return json(
            ended
              ? []
              : [
                  {
                    ...base,
                    id: 'r1',
                    state: 'running',
                    worker: 'soyo-02',
                    startedAt: '2026-11-20T08:01:00Z',
                    pairsDone: 414,
                    pairsTotal: 828,
                  },
                ],
          );
        }

        return json(ended ? [{ ...base, id: 'r1', state: 'finished', result: line }] : []);
      },
    });
    renderApp('/runs');

    const going = await screen.findByRole('listitem', { name: 'Прогон H0001 r1' });
    expect(going).toHaveTextContent('soyo-02: 414 из 828 пар');
    expect(screen.queryByRole('row', { name: /Законченный/ })).toBeNull();

    ended = true;
    await act(() => vi.advanceTimersByTimeAsync(3100));

    const done = await screen.findByRole('row', { name: 'Законченный H0001 r1' });
    expect(done).toHaveTextContent('Данных мало');
    expect(done).toHaveTextContent('0.74');
    expect(screen.queryByRole('listitem', { name: 'Прогон H0001 r1' })).toBeNull();
    expect(screen.getByText('Очередь пуста.')).toBeInTheDocument();
  });

  it('cancels a run with «Отменить»', async () => {
    const requests = fakeApi({
      'GET /api/runs': (request) =>
        json(
          new URL(request.url).searchParams.get('active') === 'true'
            ? [{ ...base, id: 'r2', state: 'queued' }]
            : [],
        ),
      'POST /api/runs/r2/cancel': () => new Response(null, { status: 204 }),
    });
    renderApp('/runs');

    const waiting = await screen.findByRole('listitem', { name: 'Прогон H0001 r2' });
    expect(waiting).toHaveTextContent('ждёт');
    await userEvent.click(within(waiting).getByRole('button', { name: 'Отменить' }));

    await screen.findByRole('listitem', { name: 'Прогон H0001 r2' });
    expect(requests.some((request) => request.url.endsWith('/api/runs/r2/cancel'))).toBe(true);
  });
});

describe('the page of a run', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Упавший прогон».
  it('shows the reason of a failed run and no report', async () => {
    fakeApi({
      'GET /api/runs/r3': () =>
        json({
          ...base,
          id: 'r3',
          state: 'finished',
          result: { ...line, status: 'failed', reason: 'the history stream broke', report: null },
        }),
    });
    renderApp('/runs/r3');

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Прогон упал: the history stream broke',
    );
    expect(screen.queryByRole('region', { name: 'Отчёт' })).toBeNull();
    expect(screen.getByLabelText('Строка журнала')).toHaveTextContent('Статус');
  });

  it('shows the line of the ledger and the report', async () => {
    fakeApi({
      'GET /api/runs/r4': () =>
        json({
          ...base,
          id: 'r4',
          state: 'finished',
          result: { ...line, report: '# H0001 funding-short-4h: exploration\n\nТекст.' },
        }),
    });
    renderApp('/runs/r4');

    const ledger = await screen.findByLabelText('Строка журнала');
    expect(ledger).toHaveTextContent('Версия движка2');
    expect(ledger).toHaveTextContent('Хэш данныхaa9c7db8');
    expect(ledger).toHaveTextContent('Попыток1');
    expect(ledger).toHaveTextContent('Мощность0.74');
    expect(
      screen.getByRole('heading', { name: 'H0001 funding-short-4h: exploration' }),
    ).toBeVisible();
  });

  it('says that a run goes when it has no line yet', async () => {
    fakeApi({
      'GET /api/runs/r5': () =>
        json({
          ...base,
          id: 'r5',
          state: 'running',
          worker: 'soyo-02',
          pairsDone: 10,
          pairsTotal: 828,
        }),
    });
    renderApp('/runs/r5');

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Прогон идёт: soyo-02: 10 из 828 пар',
    );
  });
});

describe('the report', () => {
  it('renders a pipe table as a table, with a blockquote and a heading', () => {
    render(
      <Markdown
        text={[
          '# Заголовок',
          '',
          '> **Выжившие пары.** Текст `кода`.',
          '',
          'Абзац.',
          '',
          '| Блок | Событий |',
          '|---|---|',
          '| 2025-11-17 | 55 |',
          '| 2025-11-24 | 105 |',
        ].join('\n')}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Заголовок' })).toBeInTheDocument();
    expect(screen.getByText('Выжившие пары.').tagName).toBe('STRONG');
    expect(screen.getByText('кода').tagName).toBe('CODE');
    const table = screen.getByRole('table');
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Блок', 'Событий']);
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getByRole('cell', { name: '105' })).toBeInTheDocument();
  });
});
