import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { sideLabel, typeLabel } from '@/modules/ideas';
import { setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';
import { formatClock, formatMoment, utcDayStart } from '@/shared/format/format';
import { Pager } from '@/shared/ui/Pager';

import { refusalsQuery } from '../api';
import { reasonLabel, refusalModeLabel } from '../model/labels';
import { refusalDetailText } from '../model/refusal';

const periods = [
  { value: 'today', label: 'Сегодня' },
  { value: '7d', label: '7 дней' },
] as const;

const cell = 'px-2 py-2.5';
const heads = ['Время', 'Автомат', 'Режим', 'Идея', 'Пара', 'Причина', 'Подробно'];

/** The ideas the autos saw and did not take, from the newest, with what stopped them; `autoId` limits them to one auto. */
export function RefusalsBlock({ autoId }: { autoId: string }) {
  const [period, setPeriod] = useState<string>('today');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const refusals = useQuery(refusalsQuery({ period, autoId, page, pageSize }));
  const data = refusals.data;

  return (
    <section aria-labelledby="refusals" className="space-y-2">
      <div className="flex flex-wrap items-baseline gap-3">
        <h2 id="refusals" className="text-lg font-semibold">
          Отказы автоматов
        </h2>
        <span className="text-xs text-faint">
          {period === 'today' ? 'сегодня' : 'за 7 дней'}
          {data && ` ${data.total}`} ·{' '}
          {period === 'today' ? `с ${formatClock(utcDayStart(Date.now()))} · ` : ''}новые сверху ·
          идеи, которые автомат увидел и не взял
        </span>
        <div
          role="radiogroup"
          aria-label="Период отказов"
          className="ml-auto flex rounded-lg bg-panel p-0.5 text-sm"
        >
          {periods.map((item) => (
            <button
              key={item.value}
              type="button"
              role="radio"
              aria-checked={period === item.value}
              onClick={() => {
                setPeriod(item.value);
                setPage(1);
              }}
              className={`h-8 rounded-md px-3 ${period === item.value ? 'bg-panel-2 font-bold text-text' : 'text-muted'}`}
            >
              {item.label}
            </button>
          ))}
        </div>
      </div>
      {refusals.isError && <p role="alert">Не удалось загрузить отказы.</p>}
      {data && Number(data.total) === 0 && (
        <p className="text-sm text-muted">
          {period === 'today' ? 'Сегодня отказов не было.' : 'За 7 дней отказов не было.'}
        </p>
      )}
      {data && Number(data.total) > 0 && (
        <>
          <div className="overflow-x-auto">
            <table
              aria-label="Отказы автоматов"
              className="w-full border-separate border-spacing-y-1 text-sm"
            >
              <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
                <tr>
                  {heads.map((name) => (
                    <th key={name} scope="col" className={cell}>
                      {name}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.items.map((refusal) => (
                  <tr key={refusal.id} className="bg-panel">
                    <td
                      className={`${cell} font-mono whitespace-nowrap text-muted`}
                      title={formatMoment(refusal.at)}
                    >
                      {period === 'today' ? formatClock(refusal.at) : formatMoment(refusal.at)}
                    </td>
                    <td className={`${cell} font-semibold`}>{refusal.autoName}</td>
                    <td className={`${cell} whitespace-nowrap`}>
                      <span
                        className={`rounded border px-1.5 py-px text-[11px] ${refusal.mode === 'live' ? 'border-accent text-accent' : 'border-run text-run'}`}
                      >
                        {refusalModeLabel(refusal.mode)}
                      </span>
                    </td>
                    <td className={cell}>
                      {setupLabel(refusal.setup)} · {intervalLabel(refusal.interval)} ·{' '}
                      {typeLabel(refusal.type)} · {sideLabel(refusal.side)}
                    </td>
                    <td className={`${cell} font-semibold`}>{refusal.symbol}</td>
                    <td className={`${cell} text-warn`}>
                      {refusal.reasons.map(reasonLabel).join(', ')}
                    </td>
                    <td className={`${cell} text-muted`}>{refusalDetailText(refusal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {Number(data.total) > pageSize && (
            <Pager
              page={Number(data.page)}
              pageSize={pageSize}
              total={Number(data.total)}
              onPage={setPage}
              onPageSize={(size) => {
                setPageSize(size);
                setPage(1);
              }}
            />
          )}
        </>
      )}
    </section>
  );
}
