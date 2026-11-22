import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
    expect(going).toHaveTextContent('414 из 828 пар · 50 %');
    expect(going).toHaveTextContent('soyo-02');
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

const activeOnly = (runs: unknown[], finished: unknown[] = []) => ({
  'GET /api/runs': (request: Request) =>
    json(new URL(request.url).searchParams.get('active') === 'true' ? runs : finished),
});

// Spec bench-run-queue «Ход идущего прогона полосой».
describe('the progress of a run that goes', () => {
  afterEach(() => vi.unstubAllGlobals());

  const going = { ...base, id: 'r1', state: 'running', worker: 'soyo-02' };

  // Scenario «Середина прогона».
  it('fills the bar by the share of the pairs and says «N из M пар» with the percent', async () => {
    fakeApi(
      activeOnly([
        { ...going, startedAt: '2026-11-20T08:01:00Z', pairsDone: 414, pairsTotal: 828 },
      ]),
    );
    renderApp('/runs');

    const run = within(await screen.findByRole('listitem', { name: 'Прогон H0001 r1' }));
    expect(run.getByText('414 из 828 пар · 50 %')).toBeInTheDocument();
    const bar = run.getByRole('progressbar', { name: 'Ход прогона' });
    expect(bar).toHaveAttribute('aria-valuenow', '50');
    expect(bar.firstElementChild).toHaveStyle({ width: '50%' });
    expect(run.getByText('Идёт')).toHaveAttribute('data-tone', 'run');
    expect(run.getByText(/soyo-02 · с 08:01 UTC/)).toBeInTheDocument();
  });

  // Scenario «Число пар ещё неизвестно».
  it('keeps the bar empty and counts the pairs passed while the number of pairs is not known', async () => {
    fakeApi(activeOnly([{ ...going, pairsDone: 0, pairsTotal: null }]));
    renderApp('/runs');

    const run = within(await screen.findByRole('listitem', { name: 'Прогон H0001 r1' }));
    expect(run.getByText('0 пар')).toBeInTheDocument();
    const bar = run.getByRole('progressbar', { name: 'Ход прогона' });
    expect(bar).not.toHaveAttribute('aria-valuenow');
    expect(bar.firstElementChild).toHaveStyle({ width: '0%' });
  });

  it('shows a run that waits without a bar, with its place in the queue in words', async () => {
    fakeApi(
      activeOnly([
        { ...going, pairsDone: 3, pairsTotal: 828 },
        { ...base, id: 'r2', state: 'queued', kind: 'parity' },
        { ...base, id: 'r3', state: 'queued', period: 'holdout' },
      ]),
    );
    renderApp('/runs');

    const next = within(await screen.findByRole('listitem', { name: 'Прогон H0001 r2' }));
    expect(next.queryByRole('progressbar')).toBeNull();
    expect(next.getByText('В очереди')).toHaveAttribute('data-tone', 'neutral');
    expect(next.getByText('ждёт, начнётся после текущего')).toBeInTheDocument();
    expect(next.getByText('разведка · сверка')).toBeInTheDocument();
    const last = within(screen.getByRole('listitem', { name: 'Прогон H0001 r3' }));
    expect(last.getByText('ждёт, перед ним в очереди 1')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Ждут и идут 3' })).toBeInTheDocument();
  });
});

// Spec bench-run-queue «Строка законченного прогона».
describe('the row of a finished run', () => {
  afterEach(() => vi.unstubAllGlobals());

  const long =
    'Сервис рыночных данных не отдал историю: соединение разорвано на 312-й паре из 831, повтор чтения минутных свечей и фандинга не помог, и прогон остановлен до вердикта — строка журнала записана как упавшая.';

  const finished = [
    {
      ...base,
      id: 'r9',
      state: 'finished',
      finishedAt: '2026-11-21T02:14:00Z',
      result: { ...line, status: 'failed', reason: long, power: null, readSeconds: null },
    },
    {
      ...base,
      id: 'r8',
      state: 'finished',
      finishedAt: '2026-11-21T01:40:00Z',
      result: { ...line, status: 'rejected', reason: 't 1.20 < 3.00', t: 1.2 },
    },
    {
      ...base,
      id: 'r7',
      state: 'finished',
      finishedAt: '2026-11-21T00:01:00Z',
      result: { ...line, time: '2026-11-21T00:01:00Z' },
    },
  ];

  // Scenario «Длинная причина»; spec bench-ui «Цвет статуса прогона», scenario «Упавший и отвергнутый прогоны».
  it('keeps a long reason on one line with the whole text in the hint, and colors the status', async () => {
    expect(long.length).toBeGreaterThanOrEqual(200);
    fakeApi(activeOnly([], finished));
    renderApp('/runs');

    const failed = await screen.findByRole('row', { name: 'Законченный H0001 r9' });
    expect(failed).toHaveAttribute('title', long);
    const reason = within(failed).getByText(long);
    expect(reason.className).toContain('truncate');
    expect(within(failed).getByText('Упала')).toHaveAttribute('data-tone', 'err');
    const rejected = screen.getByRole('row', { name: 'Законченный H0001 r8' });
    expect(within(rejected).getByText('Отвергнута')).toHaveAttribute('data-tone', 'neutral');
    const short = screen.getByRole('row', { name: 'Законченный H0001 r7' });
    expect(within(short).getByText('Данных мало')).toHaveAttribute('data-tone', 'warn');
    // Every row is of the same height, whatever its reason.
    expect(failed.className).toBe(short.className);
    expect(failed.className).toContain('h-13');
    expect(
      screen
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent)
        .slice(0, 9),
    ).toEqual([
      'Гипотеза',
      'Период',
      'Вид',
      'Статус',
      'Вердикт',
      't',
      'Мощность',
      'Время',
      'Закончен',
    ]);
    expect(short).toHaveTextContent('21.11 00:01');
    expect(screen.getByRole('heading', { name: 'Законченные 3' })).toBeInTheDocument();
  });

  // Scenario «Открытие с клавиатуры».
  it('opens the page of the run by Enter on the focused row and by a click anywhere in it', async () => {
    fakeApi({ ...activeOnly([], finished), 'GET /api/runs/r8': () => json(finished[1]) });
    const { history } = renderApp('/runs');

    const row = await screen.findByRole('row', { name: 'Законченный H0001 r8' });
    const link = within(row).getByRole('link', { name: /H0001/ });
    expect(link).toHaveAttribute('href', '/runs/r8');
    // The link is stretched over the whole row: its pseudo-element covers the row, which is its containing block.
    expect(link.className).toContain('after:absolute');
    expect(link.className).toContain('after:inset-0');
    expect(row.className).toContain('relative');
    link.focus();
    await userEvent.keyboard('{Enter}');

    await waitFor(() => expect(history.location.pathname).toBe('/runs/r8'));
    expect(await screen.findByText('t 1.20 < 3.00')).toBeInTheDocument();
  });
});

// Spec bench-run-queue «Числа вердикта над отчётом».
describe('the numbers of the verdict above the report', () => {
  afterEach(() => vi.unstubAllGlobals());

  const tile = (name: string) => within(screen.getByRole('group', { name }));

  // Scenario «Данных мало»; spec bench-ui «Цвет статуса прогона», scenario «Один статус на двух страницах».
  it('shows the status with the reason and the numbers of the line, a dash for a number that is not there', async () => {
    fakeApi({
      'GET /api/runs/r6': () =>
        json({
          ...base,
          id: 'r6',
          state: 'finished',
          finishedAt: '2026-11-21T00:01:00Z',
          result: {
            ...line,
            reason: 'power 0.13 < 0.8 for effect 0.003',
            power: 0.13,
            requiredT: 3,
            blocks: 54,
            events: 3366,
            t: null,
            readSeconds: 787,
            report: '# H0001 funding-short-4h: exploration\n\nТекст.',
          },
        }),
    });
    renderApp('/runs/r6');

    const verdict = await screen.findByRole('region', { name: 'Вердикт' });
    expect(verdict).toHaveAttribute('data-tone', 'warn');
    expect(within(verdict).getByText('Данных мало')).toHaveAttribute('data-tone', 'warn');
    expect(verdict).toHaveTextContent('power 0.13 < 0.8 for effect 0.003');
    expect(tile('Мощность').getByText('0.13')).toBeInTheDocument();
    expect(tile('Мощность').getByText('нужно 0.80')).toBeInTheDocument();
    expect(tile('Мощность').getByRole('meter')).toHaveAttribute('aria-valuenow', '0.13');
    expect(tile('t').getByText('—')).toBeInTheDocument();
    expect(tile('t').getByText('нужно 3.00')).toBeInTheDocument();
    expect(tile('Блоков').getByText('54')).toBeInTheDocument();
    expect(tile('Событий').getByText('3 366')).toBeInTheDocument();
    expect(tile('Попыток').getByText('1')).toBeInTheDocument();
    expect(tile('Чтение и расчёт').getByText('787 с')).toBeInTheDocument();
    expect(tile('Чтение и расчёт').getByText('13 мин 7 с')).toBeInTheDocument();
    // The numbers stand above the report.
    const report = screen.getByRole('region', { name: 'Отчёт' });
    expect(
      screen.getByRole('group', { name: 'Мощность' }).compareDocumentPosition(report) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  // Scenario «Упавший прогон».
  it('shows the whole reason of a failed run in the color of an error, dashes for the numbers and no report', async () => {
    const reason = 'the history stream broke: '.padEnd(220, 'x');
    fakeApi({
      'GET /api/runs/r3': () =>
        json({
          ...base,
          id: 'r3',
          state: 'finished',
          result: {
            ...line,
            status: 'failed',
            reason,
            attempts: null,
            requiredT: null,
            power: null,
            blocks: null,
            events: null,
            readSeconds: null,
          },
        }),
    });
    renderApp('/runs/r3');

    const verdict = await screen.findByRole('region', { name: 'Вердикт' });
    expect(verdict).toHaveAttribute('data-tone', 'err');
    expect(within(verdict).getByText('Упала')).toHaveAttribute('data-tone', 'err');
    expect(within(verdict).getByText(reason)).toBeInTheDocument();
    for (const name of ['t', 'Мощность', 'Блоков', 'Событий', 'Попыток', 'Чтение и расчёт']) {
      expect(tile(name).getByText('—')).toBeInTheDocument();
    }

    expect(screen.queryByRole('region', { name: 'Отчёт' })).toBeNull();
  });

  it.each([
    [{ state: 'queued' }, 'В очереди', 'neutral', 'Прогон ждёт своей очереди.'],
    [
      { state: 'running', worker: 'soyo-02', pairsDone: 10, pairsTotal: 828 },
      'Идёт',
      'run',
      'Прогон идёт: soyo-02: 10 из 828 пар.',
    ],
    [
      {
        state: 'finished',
        result: { ...line, status: 'cancelled', reason: 'пройдено 3 пары из 831' },
      },
      'Отменён',
      'neutral',
      'Прогон отменён: пройдено 3 пары из 831',
    ],
  ])('still says why a run has no report: %#', async (run, word, tone, why) => {
    fakeApi({ 'GET /api/runs/r5': () => json({ ...base, id: 'r5', ...run }) });
    renderApp('/runs/r5');

    expect(await screen.findByRole('status')).toHaveTextContent(why);
    const verdict = screen.getByRole('region', { name: 'Вердикт' });
    expect(verdict).toHaveAttribute('data-tone', tone);
    expect(within(verdict).getByText(word)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Отчёт' })).toBeNull();
  });

  it('names the hypothesis and leads to it when the list of hypotheses has it', async () => {
    fakeApi({
      'GET /api/runs/r6': () => json({ ...base, id: 'r6', state: 'finished', result: line }),
      'GET /api/hypotheses': () =>
        json([
          {
            id: 'H0001',
            version: 1,
            name: 'funding-short-4h',
            timeframe: '4h',
            signal: { formatVersion: 1 },
            runs: [],
          },
        ]),
    });
    renderApp('/runs/r6');

    expect(await screen.findByRole('heading', { name: /H0001 funding-short-4h/ })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Открыть гипотезу' })).toHaveAttribute(
      'href',
      '/hypotheses/H0001',
    );
  });
});

// Spec bench-run-queue «Хэши прогона копируются».
describe('the hashes of a run', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    // jsdom has no `execCommand`; a test that gave it one takes it away.
    Reflect.deleteProperty(document, 'execCommand');
  });

  // Scenario «Копирование хэша данных».
  it('shows a hash by its first and last 8 signs and copies the whole of it', async () => {
    const dataHash = '8c33b68603417c3865a5914ad872af25a83b7ab3b505ab25f645b0d6c4bab201';
    const hash = '5591d11a0f5c2b7e4d9a8c1b2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b4fa2a84f';
    const id = '74dec6c8b1a04f6e9d2c3b4aa3a386d4';
    fakeApi({
      [`GET /api/runs/${id}`]: () =>
        json({ ...base, id, state: 'finished', result: { ...line, hash, dataHash } }),
    });
    const copied: string[] = [];
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockImplementation(async (text) => {
      copied.push(text);
    });
    renderApp(`/runs/${id}`);

    const details = within(await screen.findByRole('region', { name: 'Детали' }));
    expect(details.getByText('8c33b686…c4bab201')).toBeInTheDocument();
    expect(details.getByText('5591d11a…4fa2a84f')).toBeInTheDocument();
    expect(details.getByText('74dec6c8…a3a386d4')).toBeInTheDocument();
    expect(details.queryByText(dataHash)).toBeNull();

    await user.click(details.getByRole('button', { name: 'Скопировать: Хэш данных' }));
    await user.click(details.getByRole('button', { name: 'Скопировать: Хэш гипотезы' }));
    await user.click(details.getByRole('button', { name: 'Скопировать: Прогон' }));

    expect(copied).toEqual([dataHash, hash, id]);
    expect(copied[0]).toHaveLength(64);
  });

  // The bench is served over plain HTTP from an address of the network, where a page has no `navigator.clipboard`.
  it('copies without the clipboard of a secure page, and says when it could not', async () => {
    const dataHash = '8c33b68603417c3865a5914ad872af25a83b7ab3b505ab25f645b0d6c4bab201';
    const id = '74dec6c8b1a04f6e9d2c3b4aa3a386d4';
    fakeApi({
      [`GET /api/runs/${id}`]: () =>
        json({ ...base, id, state: 'finished', result: { ...line, dataHash } }),
    });
    vi.stubGlobal('navigator', { userAgent: navigator.userAgent });
    const copied: string[] = [];
    let works = true;
    document.execCommand = vi.fn((command: string) => {
      if (command === 'copy' && works) {
        copied.push((document.activeElement as HTMLTextAreaElement).value);
      }

      return works;
    });
    renderApp(`/runs/${id}`);
    const details = within(await screen.findByRole('region', { name: 'Детали' }));
    const button = details.getByRole('button', { name: 'Скопировать: Хэш данных' });

    fireEvent.click(button);

    await waitFor(() => expect(button).toHaveAttribute('title', 'Скопировано'));
    expect(copied).toEqual([dataHash]);
    expect(document.querySelector('textarea')).toBeNull();

    works = false;
    fireEvent.click(details.getByRole('button', { name: 'Скопировать: Прогон' }));

    await waitFor(() =>
      expect(details.getByRole('button', { name: 'Скопировать: Прогон' })).toHaveAttribute(
        'title',
        'Не удалось скопировать',
      ),
    );
    expect(copied).toEqual([dataHash]);
  });
});

// Spec bench-run-queue «Длинная таблица отчёта».
describe('a long table of the report', () => {
  const table = (rows: number) =>
    [
      '## «Ложные события» за блоки',
      '',
      '| Блок | Событий | Среднее |',
      '|---|---|---|',
      ...Array.from(
        { length: rows },
        (_, index) => `| 2023-09-${String(index + 1).padStart(2, '0')} | ${index} | 0.00043 |`,
      ),
    ].join('\n');

  // Scenario «Блоки отчёта».
  it('scrolls a table of more than 12 rows inside its own area, with its head in place', () => {
    render(<Markdown text={table(54)} />);

    const area = screen.getByRole('table').parentElement!;
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(55);
    expect(area).toHaveAttribute('data-scroll', 'true');
    expect(area.className).toContain('overflow-y-auto');
    expect(area.className).toMatch(/max-h-/);
    expect(area).toHaveAttribute('tabindex', '0');
    expect(screen.getByRole('table').querySelector('thead')!.className).toContain('sticky');
    expect(screen.getByText('54 строки')).toBeInTheDocument();
  });

  // Scenario «Короткая таблица».
  it('shows a table of 12 rows or fewer whole', () => {
    const { unmount } = render(<Markdown text={table(5)} />);
    let area = screen.getByRole('table').parentElement!;
    expect(area).toHaveAttribute('data-scroll', 'false');
    expect(area.className).not.toContain('overflow-y-auto');
    expect(area.className).not.toMatch(/max-h-/);
    unmount();

    render(<Markdown text={table(12)} />);
    area = screen.getByRole('table').parentElement!;
    expect(area).toHaveAttribute('data-scroll', 'false');
  });

  it('draws a quote as a block with a bar on the left', () => {
    render(
      <Markdown text={'> **Выжившие пары.** Стенд видит только пары, что торгуются сейчас.'} />,
    );

    const quote = screen.getByText('Выжившие пары.').closest('blockquote')!;
    expect(quote.className).toContain('border-l-2');
    expect(quote.className).toContain('border-warn');
  });
});
