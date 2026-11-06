import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import {
  ClosedTradesTable,
  ideaJournalQuery,
  type JournalFilters,
  JournalTotals,
  loadJournalChoice,
  OpenTradesTable,
  saveJournalChoice,
} from '@/modules/idea-trades';
import { sideLabel } from '@/modules/ideas';
import { setupChoices } from '@/modules/market';
import { groupDigits } from '@/shared/format/format';
import { useDebounced } from '@/shared/hooks/useDebounced';
import { Pager } from '@/shared/ui/Pager';

const control = 'h-9 rounded-lg border border-line bg-panel px-2 text-sm';

const badges = [
  { value: 'up', label: '▲' },
  { value: 'flat', label: '≈' },
  { value: 'down', label: '▼' },
  { value: 'none', label: 'без значка' },
];

const periods = [
  { value: '7', label: '7 дней' },
  { value: '30', label: '30 дней' },
  { value: '90', label: '90 дней' },
];

function Select({
  label,
  all = 'все',
  value,
  onChange,
  options,
}: {
  label: string;
  all?: string;
  value: string;
  onChange: (value: string) => void;
  options: readonly { value: string; label: string }[];
}) {
  return (
    <select
      aria-label={label}
      value={value}
      onChange={(event) => onChange(event.target.value)}
      className={control}
    >
      <option value="">
        {label}: {all}
      </option>
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

/**
 * The journal of the trades by ideas: the trades in work with what they are worth now, the totals of the closed ones of the filter and
 * a page of them. The API filters, cuts and counts; the address holds the number of the page, the browser the filters and the size.
 */
export function IdeaJournalPage({ page }: { page: number }) {
  const navigate = useNavigate();
  const [remembered] = useState(loadJournalChoice);
  const [filters, setFilters] = useState<JournalFilters>(remembered.filters);
  const [pageSize, setPageSize] = useState(remembered.pageSize);
  // The pair goes to the API after a pause in the typing.
  const [symbolText, setSymbolText] = useState(remembered.filters.symbol);
  const symbol = useDebounced(symbolText, 300);
  const journal = useQuery(ideaJournalQuery({ filters: { ...filters, symbol }, page, pageSize }));
  const answered = journal.data?.closed.page;
  const showPage = (number: number, replace = false) =>
    void navigate({
      to: '/idea-trades',
      search: { page: number === 1 ? undefined : number },
      replace,
    });
  // Any other choice starts the closed trades over from their first page.
  const firstPage = () => {
    if (page !== 1) {
      showPage(1, true);
    }
  };
  // The list may have become shorter: the address follows the page the API gave.
  useEffect(() => {
    if (answered !== undefined && !journal.isPlaceholderData && Number(answered) !== page) {
      showPage(Number(answered), true);
    }
  }, [answered, journal.isPlaceholderData, page]);
  useEffect(
    () => saveJournalChoice({ filters: { ...filters, symbol: symbolText }, pageSize }),
    [filters, symbolText, pageSize],
  );
  const set = (patch: Partial<JournalFilters>) => {
    setFilters((current) => ({ ...current, ...patch }));
    firstPage();
  };

  return (
    <section className="space-y-4">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Журнал</h1>
        <p className="text-sm text-muted">
          Сделки по идеям: вход, стоп и цель стоят на бирже, сделку ведёт торговый Worker. Войти по
          идее можно со страницы пары.
        </p>
      </header>
      <div className="flex flex-wrap items-center gap-3">
        <Select
          label="Сетап"
          value={filters.setup}
          onChange={(setup) => set({ setup })}
          options={setupChoices}
        />
        <input
          aria-label="Пара"
          placeholder="Пара"
          value={symbolText}
          onChange={(event) => {
            setSymbolText(event.target.value);
            firstPage();
          }}
          className={`${control} w-32`}
        />
        <Select
          label="Сторона"
          value={filters.side}
          onChange={(side) => set({ side })}
          options={[
            { value: 'long', label: sideLabel('long') },
            { value: 'short', label: sideLabel('short') },
          ]}
        />
        <Select
          label="Значок"
          value={filters.badge}
          onChange={(badge) => set({ badge })}
          options={badges}
        />
        <Select
          label="Период"
          all="всё время"
          value={filters.days}
          onChange={(days) => set({ days })}
          options={periods}
        />
        <Link to="/idea-trades/settings" className="ml-auto text-sm text-run hover:underline">
          Настройки
        </Link>
      </div>
      {journal.isError && <p role="alert">Не удалось загрузить журнал.</p>}
      {journal.isPending && <p className="text-sm text-muted">Загрузка журнала…</p>}
      {journal.data && (
        <>
          <section aria-labelledby="in-work" className="space-y-2">
            <h2 id="in-work" className="text-lg font-semibold">
              В работе
            </h2>
            {journal.data.open.length === 0 ? (
              <p className="text-sm text-muted">В работе ничего нет.</p>
            ) : (
              <OpenTradesTable trades={journal.data.open} />
            )}
          </section>
          <section aria-labelledby="closed" className="space-y-2">
            <h2 id="closed" className="text-lg font-semibold">
              Закрытые сделки
            </h2>
            <JournalTotals totals={journal.data.totals} />
            {journal.data.closed.items.length === 0 ? (
              <p className="text-sm text-muted">Закрытых сделок по этим условиям нет.</p>
            ) : (
              <>
                <ClosedTradesTable trades={journal.data.closed.items} />
                <Pager
                  page={Number(journal.data.closed.page)}
                  pageSize={Number(journal.data.closed.pageSize)}
                  total={Number(journal.data.closed.total)}
                  onPage={showPage}
                  onPageSize={(size) => {
                    setPageSize(size);
                    firstPage();
                  }}
                />
                <p className="text-xs text-faint">
                  Всего закрытых: {groupDigits(String(journal.data.closed.total))}. Обещание — то,
                  что идея обещала при входе; идея — чем она закончилась сама.
                </p>
              </>
            )}
          </section>
        </>
      )}
    </section>
  );
}
