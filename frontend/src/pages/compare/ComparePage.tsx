import { useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useRef, useState } from 'react';

import { engineConflict, resultColumns, whyDetailsUnavailable } from '@/modules/backtests';
import {
  type ComparisonItem,
  formatItems,
  parameterRows,
  parseItems,
  replaceComparison,
} from '@/modules/comparison';
import { EquityComparisonChart, type EquityLine } from '@/modules/market-chart';
import { formatMoment } from '@/shared/format/format';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';

import { useCompared } from './useCompared';

const rowHead = 'py-2 pr-4 text-left text-sm font-normal text-muted';
const tones = { ok: 'text-ok', err: 'text-err' };
const keyOf = (item: ComparisonItem) => `${item.backtestId}:${item.combination}`;
const colorOf = (index: number) => `--color-series-${index + 1}`;

/** The results of the set side by side, with their equity curves on one chart. The address holds the set. */
export function ComparePage({ items: address }: { items: string | undefined }) {
  const navigate = useNavigate();
  const items = useMemo(() => parseItems(address), [address]);
  const compared = useCompared(items);
  const [notices, setNotices] = useState<string[]>([]);
  const handled = useRef(new Set<string>());

  // Opening an address replaces the set of the tab with the set of the address.
  useEffect(() => {
    replaceComparison(items);
  }, [items]);

  function show(remaining: readonly ComparisonItem[]) {
    void navigate({
      to: '/compare',
      search: { items: remaining.length > 0 ? formatItems(remaining) : undefined },
      replace: true,
    });
  }

  const missing = compared.filter(
    (entry) => entry.missing && !handled.current.has(keyOf(entry.item)),
  );

  useEffect(() => {
    if (missing.length === 0) {
      return;
    }

    missing.forEach((entry) => handled.current.add(keyOf(entry.item)));
    setNotices((shown) => [
      ...shown,
      ...missing.map(
        ({ item }) =>
          `Результат #${item.combination} бэктеста ${item.backtestId.slice(0, 8)} не найден — убран из набора.`,
      ),
    ]);
    show(items.filter((item) => !missing.some((entry) => keyOf(entry.item) === keyOf(item))));
  });

  const present = compared.filter((entry) => !entry.missing);
  const rows = parameterRows(
    present.map((entry) =>
      Object.fromEntries(
        Object.entries((entry.result?.parameterValues ?? {}) as Record<string, unknown>).map(
          ([name, value]) => [name, String(value)],
        ),
      ),
    ),
  );
  const nameOf = (entry: (typeof present)[number]) =>
    `${entry.bot?.name ?? ''} #${entry.item.combination}`.trim();
  const lines: EquityLine[] = present.flatMap((entry, index) =>
    entry.details
      ? [{ label: nameOf(entry), color: colorOf(index), points: entry.details.equity }]
      : [],
  );
  const withoutCurve = present.flatMap((entry) =>
    entry.detailsError
      ? [{ entry, reason: whyDetailsUnavailable(entry.detailsError.message) }]
      : [],
  );

  return (
    <section className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold">Сравнение</h1>
          <p className="text-sm text-muted">
            Набор живёт в этой вкладке и в адресе страницы — ссылкой можно поделиться.
          </p>
        </div>
        {items.length > 0 && <Button onClick={() => show([])}>Очистить набор</Button>}
      </header>
      {notices.map((notice) => (
        <p
          key={notice}
          role="status"
          className="rounded-md border border-[#6b5326] bg-warn-bg p-3 text-sm text-warn"
        >
          {notice}
        </p>
      ))}
      {present.length === 0 ? (
        <p className="text-sm text-muted">Набор пуст. Отметьте результаты в таблице бэктеста.</p>
      ) : (
        <>
          <section className="space-y-3 rounded-xl border border-line bg-panel p-5">
            <h2 className="font-bold">Параметры и метрики</h2>
            <table className="w-full table-fixed text-sm">
              <thead>
                <tr>
                  <th className="w-40" />
                  {present.map((entry, index) => (
                    <th
                      key={keyOf(entry.item)}
                      scope="col"
                      className="pb-3 text-left align-top font-normal"
                    >
                      <span className="flex items-start justify-between gap-2">
                        <span className="space-y-1">
                          <span className="flex items-center gap-2 font-bold">
                            <span
                              data-series={colorOf(index)}
                              className="h-0.5 w-3 shrink-0"
                              style={{ background: `var(${colorOf(index)})` }}
                            />
                            {entry.bot?.name}
                          </span>
                          <span className="block font-mono text-xs text-muted">
                            #{entry.item.combination}
                            {entry.backtest && ` · ${formatMoment(entry.backtest.createdAt)}`}
                          </span>
                          {entry.backtest?.botChanged && <Badge tone="warn">бот изменился</Badge>}
                          {entry.detailsError && engineConflict(entry.detailsError.message) && (
                            <Badge>
                              движок v{engineConflict(entry.detailsError.message)!.computed}
                            </Badge>
                          )}
                        </span>
                        <button
                          type="button"
                          aria-label={`Убрать результат #${entry.item.combination} из сравнения`}
                          className="text-muted hover:text-text"
                          onClick={() =>
                            show(items.filter((item) => keyOf(item) !== keyOf(entry.item)))
                          }
                        >
                          ✕
                        </button>
                      </span>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.name}>
                    <th scope="row" className={`${rowHead} font-mono`}>
                      {row.name}
                    </th>
                    {row.values.map((value, index) => (
                      <td key={index} className="py-2 font-mono">
                        {value}
                      </td>
                    ))}
                  </tr>
                ))}
                {resultColumns.map((column, columnIndex) => (
                  <tr key={column.code} className={columnIndex === 0 ? 'border-t border-line' : ''}>
                    <th scope="row" className={rowHead}>
                      {column.title}
                    </th>
                    {present.map((entry) => {
                      const tone = entry.result && column.tone?.(entry.result.metrics);

                      return (
                        <td
                          key={keyOf(entry.item)}
                          title={entry.result ? column.exact(entry.result.metrics) : undefined}
                          className={`py-2 font-mono whitespace-nowrap ${tone ? tones[tone] : ''}`}
                        >
                          {entry.result ? column.show(entry.result.metrics) : '—'}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="text-xs text-faint">
              Строки параметров — объединение параметров всех результатов; «—» значит, что у
              стратегии результата такого параметра нет.
            </p>
          </section>
          <section className="space-y-3 rounded-xl border border-line bg-panel p-5">
            <h2 className="font-bold">Капитал</h2>
            <EquityComparisonChart lines={lines} label="Капитал" />
            {present.some((entry) => entry.detailsLoading) && (
              <p className="text-xs text-faint">Кривые загружаются…</p>
            )}
            {withoutCurve.map(({ entry, reason }) => (
              <p key={keyOf(entry.item)} className="rounded bg-app px-3 py-1.5 text-xs text-muted">
                {nameOf(entry)}: кривой нет — {reason}
              </p>
            ))}
          </section>
        </>
      )}
      <p className="text-xs text-faint">
        Кривые загружаются по одной из деталей каждого результата: таблица видна сразу, кривые
        дорисовываются.
      </p>
    </section>
  );
}
