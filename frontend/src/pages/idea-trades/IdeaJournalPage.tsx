import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { autosQuery, DailyStopBanner, RefusalsBlock } from '@/modules/idea-autos';
import {
  closedCountText,
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
const origins = [
  { value: '', label: 'Все' },
  { value: 'manual', label: 'Ручные' },
  { value: 'auto', label: 'Авто' },
  { value: 'paper', label: 'Учебные' },
];

const realCaptions: Record<string, string> = {
  '': 'ручные и автоматы',
  manual: 'ручные',
  auto: 'автоматы',
};

export function IdeaJournalPage({ page, autoId }: { page: number; autoId?: string | undefined }) {
  const navigate = useNavigate();
  const [remembered] = useState(loadJournalChoice);
  // The count of refusals on «Автоматы» opens the journal on its auto, whatever was kept in the browser.
  const [filters, setFilters] = useState<JournalFilters>(
    autoId === undefined ? remembered.filters : { ...remembered.filters, autoId },
  );
  const autos = useQuery(autosQuery());
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
  // The address has given its auto to the filter; the filter is what stays.
  useEffect(() => {
    if (autoId !== undefined) {
      void navigate({
        to: '/idea-trades',
        search: { page: page === 1 ? undefined : page },
        replace: true,
      });
    }
  }, []);
  useEffect(
    () => saveJournalChoice({ filters: { ...filters, symbol: symbolText }, pageSize }),
    [filters, symbolText, pageSize],
  );
  // An auto that is gone is still a filter the owner may have kept; it stays among the options under a name of its own.
  const autoOptions = [
    ...(autos.data ?? []).map((item) => ({ value: item.auto.id, label: item.auto.name })),
    ...(filters.autoId === '' ||
    autos.data === undefined ||
    autos.data.some((item) => item.auto.id === filters.autoId)
      ? []
      : [{ value: filters.autoId, label: 'удалённый автомат' }]),
  ];
  const set = (patch: Partial<JournalFilters>) => {
    setFilters((current) => ({ ...current, ...patch }));
    firstPage();
  };

  return (
    <section className="space-y-4">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Сделки</h1>
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
        <div
          role="radiogroup"
          aria-label="Кто взял"
          className="flex items-center gap-2 text-sm text-muted"
        >
          Кто взял
          <span className="flex rounded-lg bg-panel p-0.5">
            {origins.map((origin) => (
              <button
                key={origin.value}
                type="button"
                role="radio"
                aria-checked={filters.origin === origin.value}
                onClick={() => set({ origin: origin.value })}
                className={`h-8 rounded-md px-3 ${filters.origin === origin.value ? 'bg-panel-2 font-bold text-text' : 'text-muted hover:text-text'}`}
              >
                {origin.label}
              </button>
            ))}
          </span>
        </div>
        <Select
          label="Автомат"
          value={filters.autoId}
          onChange={(next) => set({ autoId: next })}
          options={autoOptions}
        />
        <span className="ml-auto flex gap-3 text-sm">
          <Link to="/idea-trades/autos" className="text-run hover:underline">
            Автоматы
          </Link>
          <Link to="/idea-trades/settings" className="text-run hover:underline">
            Настройки
          </Link>
        </span>
      </div>
      <DailyStopBanner />
      {journal.isError && <p role="alert">Не удалось загрузить сделки.</p>}
      {journal.isPending && <p className="text-sm text-muted">Загрузка сделок…</p>}
      {journal.data && (
        <>
          <section aria-labelledby="in-work" className="space-y-2">
            <h2 id="in-work" className="text-lg font-semibold">
              В работе
              {journal.data.open.length > 0 && (
                <span className="ml-3 text-xs font-normal text-faint">
                  {journal.data.open.length} · с деньгами{' '}
                  {journal.data.open.filter((trade) => trade.origin !== 'paper').length} · учебных{' '}
                  {journal.data.open.filter((trade) => trade.origin === 'paper').length}
                </span>
              )}
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
            <div className="grid gap-3 lg:grid-cols-2">
              {filters.origin !== 'paper' && (
                <JournalTotals
                  title="С деньгами"
                  caption={`${realCaptions[filters.origin]} · ${closedCountText(journal.data.totals)}`}
                  totals={journal.data.totals}
                />
              )}
              {(filters.origin === '' || filters.origin === 'paper') && (
                <JournalTotals
                  title="Учебные"
                  caption={`без ордеров · ${closedCountText(journal.data.paperTotals)} · в защиты не входят`}
                  label="Итоги учебных сделок"
                  tone="paper"
                  totals={journal.data.paperTotals}
                />
              )}
            </div>
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
          <RefusalsBlock autoId={filters.autoId} />
        </>
      )}
    </section>
  );
}
