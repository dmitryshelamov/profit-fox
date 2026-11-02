import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';

import { backtestResultsQuery, rangedNames, resultColumns } from '@/modules/backtests';
import { CompareCheckbox } from '@/modules/comparison';
import { Button } from '@/shared/ui/Button';

const PAGE = 100;
const head = 'px-4 py-3 text-left text-[11px] font-semibold tracking-wide text-faint uppercase';
const cell = 'px-4 py-2.5 font-mono';
const tones = { ok: 'text-ok', err: 'text-err' };

/** The results of a backtest: sorted on the server, by pages of 100, each result markable for comparison. */
export function ResultsTable({
  backtestId,
  parameters,
  finished,
  combinations,
}: {
  backtestId: string;
  parameters: Readonly<Record<string, unknown>>;
  finished: boolean;
  combinations: number | string;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [sort, setSort] = useState({ code: 'return', order: 'desc' as 'asc' | 'desc' });
  const [offset, setOffset] = useState(0);
  const results = useQuery({
    ...backtestResultsQuery(
      backtestId,
      { sort: sort.code, order: sort.order, offset, limit: PAGE },
      !finished,
    ),
    placeholderData: keepPreviousData,
  });
  // The last poll may come before the backtest is seen finished, so the page it shows is refreshed once more.
  const wasFinished = useRef(finished);
  useEffect(() => {
    if (finished && !wasFinished.current) {
      void queryClient.invalidateQueries({ queryKey: ['backtests', backtestId, 'results'] });
    }

    wasFinished.current = finished;
  }, [finished, backtestId, queryClient]);
  const names = rangedNames(parameters);
  const total = Number(results.data?.total ?? 0);

  function sortBy(code: string, best: 'asc' | 'desc') {
    setOffset(0);
    setSort(
      sort.code === code
        ? { code, order: sort.order === 'asc' ? 'desc' : 'asc' }
        : { code, order: best },
    );
  }

  function header(code: string, title: string, best: 'asc' | 'desc') {
    const active = sort.code === code;

    return (
      <th
        key={code}
        scope="col"
        aria-sort={active ? (sort.order === 'asc' ? 'ascending' : 'descending') : undefined}
        className={`${head} ${active ? 'text-text' : ''}`}
      >
        <button type="button" className="uppercase" onClick={() => sortBy(code, best)}>
          {title}
          {active && (
            <span aria-hidden className="ml-1 text-accent">
              {sort.order === 'asc' ? '↑' : '↓'}
            </span>
          )}
        </button>
      </th>
    );
  }

  return (
    <section className="space-y-3">
      <div className="flex items-baseline gap-4">
        <h2 className="text-lg font-bold">Результаты</h2>
        {!finished && (
          <span className="text-xs text-faint">
            посчитано {total} из {String(combinations)} — таблица дополняется
          </span>
        )}
      </div>
      {results.isError && <p role="alert">Не удалось загрузить результаты.</p>}
      <table className="w-full border-separate border-spacing-y-1 text-sm">
        <thead className="bg-panel">
          <tr>
            <th scope="col" className={head} />
            {header('combination', '#', 'asc')}
            {names.map((name) => (
              <th key={name} scope="col" className={head}>
                {name}
              </th>
            ))}
            {resultColumns.map((column) => header(column.code, column.title, column.best))}
          </tr>
        </thead>
        <tbody>
          {results.data?.items.map((result) => {
            const number = Number(result.combination);
            const values = result.parameterValues as Record<string, unknown>;

            return (
              <tr
                key={number}
                className="cursor-pointer bg-panel hover:bg-panel-2"
                onClick={() =>
                  void navigate({
                    to: '/backtests/$backtestId/results/$combination',
                    params: { backtestId, combination: String(number) },
                  })
                }
              >
                <td className="px-4 py-2.5">
                  <CompareCheckbox item={{ backtestId, combination: number }} />
                </td>
                <td className={`${cell} text-muted`}>
                  <Link
                    to="/backtests/$backtestId/results/$combination"
                    params={{ backtestId, combination: String(number) }}
                    onClick={(event) => event.stopPropagation()}
                  >
                    {number}
                  </Link>
                </td>
                {names.map((name) => (
                  <td key={name} className={cell}>
                    {String(values[name])}
                  </td>
                ))}
                {resultColumns.map((column) => {
                  const tone = column.tone?.(result.metrics);

                  return (
                    <td
                      key={column.code}
                      title={column.exact(result.metrics)}
                      className={`${cell} whitespace-nowrap ${tone ? tones[tone] : ''}`}
                    >
                      {column.show(result.metrics)}
                    </td>
                  );
                })}
              </tr>
            );
          })}
        </tbody>
      </table>
      <div className="flex items-center justify-between text-xs text-faint">
        <span>
          {total === 0
            ? 'Результатов пока нет.'
            : `${offset + 1} – ${Math.min(offset + PAGE, total)} из ${total}`}
        </span>
        <span className="flex gap-2">
          <Button look="plain" disabled={offset === 0} onClick={() => setOffset(offset - PAGE)}>
            ← Назад
          </Button>
          <Button
            look="plain"
            disabled={offset + PAGE >= total}
            onClick={() => setOffset(offset + PAGE)}
          >
            Вперёд →
          </Button>
        </span>
      </div>
      <p className="text-xs text-faint">
        Строка открывает детали результата. Набор сравнения живёт во вкладке и собирается из любых
        бэктестов.
      </p>
    </section>
  );
}
