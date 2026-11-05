import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useNavigate } from '@tanstack/react-router';
import { type ReactNode, useState } from 'react';

import {
  type Idea,
  ideaQuery,
  levelsOf,
  OwnIdeaBlock,
  type OwnIdeaInput,
  PairIdeasList,
  pairIdeasQuery,
  useOwnIdea,
  zonesQuery,
} from '@/modules/ideas';
import {
  CategoryBadge,
  compactNumber,
  fundingQuery,
  fundingText,
  marketPairQuery,
  openInterestQuery,
  pairIntervals,
  PercentChange,
  setupIntervals,
  shownPairInterval,
  spreadText,
  UpdatedAt,
  useNow,
  usePairCandles,
} from '@/modules/market';
import {
  BotCandleChart,
  candleLegend,
  firedMark,
  FundingChart,
  type LevelInput,
  OpenInterestChart,
  type ZoneInput,
} from '@/modules/market-chart';
import { intervalLabel } from '@/modules/market-data';
import { ApiError } from '@/shared/api/client';
import { groupDigits } from '@/shared/format/format';

// The API gives at most 1000 points: 3 days of open interest are 864 points of 5 minutes; funding settles a few times a day.
const openInterestDays = 3;
const fundingDays = 10;
const day = 86_400_000;

function PairChart({
  category,
  symbol,
  interval,
  idea,
  levels,
  zones,
  signalTime,
}: {
  category: string;
  symbol: string;
  interval: string;
  /** The selected idea, which stays in the address when the interval changes. */
  idea: string | undefined;
  levels: readonly LevelInput[] | undefined;
  zones: readonly ZoneInput[] | undefined;
  signalTime: number | undefined;
}) {
  const navigate = useNavigate();
  const history = usePairCandles(category, symbol, interval);
  const [hovered, setHovered] = useState<number | null>(null);
  const legend = candleLegend(history.candles, hovered);

  return (
    <section
      aria-label="График"
      className="space-y-3 rounded-xl border border-line bg-panel px-5 py-4"
    >
      <div role="group" aria-label="Интервал" className="flex flex-wrap gap-1">
        {pairIntervals.map((item) => (
          <button
            key={item.value}
            type="button"
            aria-pressed={item.value === interval}
            onClick={() =>
              void navigate({
                to: '/market/$category/$symbol',
                params: { category, symbol },
                search:
                  idea === undefined ? { interval: item.value } : { interval: item.value, idea },
              })
            }
            className="h-7 rounded-md px-2.5 text-xs font-semibold text-muted hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:text-text"
          >
            {item.label}
          </button>
        ))}
      </div>
      {legend && (
        <div
          role="group"
          aria-label="Свеча"
          className="flex flex-wrap gap-x-6 font-mono text-xs text-muted"
        >
          <span>{legend.time}</span>
          <span>O {legend.open}</span>
          <span>H {legend.high}</span>
          <span>L {legend.low}</span>
          <span>C {legend.close}</span>
          <span>Объём {legend.volume}</span>
        </div>
      )}
      <BotCandleChart
        history={history.candles}
        interval={interval}
        levels={levels}
        zones={zones}
        signalTime={signalTime}
        label={`График свечей ${symbol}, ${intervalLabel(interval)}`}
        onReachStart={history.loadEarlier}
        onHover={setHovered}
      />
      {history.isError && <p role="alert">Не удалось загрузить свечи.</p>}
      {history.isPending && <p className="text-sm text-muted">Загрузка свечей…</p>}
      {!history.isPending &&
        !history.isError &&
        history.candles.length === 0 &&
        !history.unavailable && <p className="text-sm text-muted">Свечей за период нет.</p>}
      {history.unavailable && (
        <p role="status" className="text-sm text-warn">
          История недоступна: Bybit не отвечает
        </p>
      )}
    </section>
  );
}

/** A side chart with its title and what it shows; a message while it has no points or failed to load. */
function SideChart({
  title,
  note,
  error,
  empty,
  children,
}: {
  title: string;
  note: string;
  error: string | null;
  empty: boolean;
  children: ReactNode;
}) {
  return (
    <section
      aria-label={title}
      className="space-y-3 rounded-xl border border-line bg-panel px-5 py-4"
    >
      <h2 className="text-sm font-semibold">
        {title} <span className="ml-2 font-normal text-faint">{note}</span>
      </h2>
      {error !== null ? (
        <p role="alert">{error}</p>
      ) : empty ? (
        <p className="flex h-50 items-center justify-center text-sm text-muted">Нет данных</p>
      ) : (
        children
      )}
    </section>
  );
}

/** Open interest of the last 3 days and funding of the last 10 days of a linear pair, side by side. */
function SideCharts({ category, symbol }: { category: string; symbol: string }) {
  const [openInterestFrom] = useState(() =>
    new Date(Date.now() - openInterestDays * day).toISOString(),
  );
  const [fundingFrom] = useState(() => new Date(Date.now() - fundingDays * day).toISOString());
  const openInterest = useQuery(openInterestQuery(category, symbol, openInterestFrom));
  const funding = useQuery(fundingQuery(category, symbol, fundingFrom));

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <SideChart
        title="Открытый интерес"
        note={`USDT · каждые 5 мин · последние ${openInterestDays} дня`}
        error={openInterest.isError ? 'Не удалось загрузить открытый интерес.' : null}
        empty={openInterest.data?.length === 0}
      >
        <OpenInterestChart
          points={openInterest.data ?? []}
          label={`Открытый интерес ${symbol}`}
          formatValue={(value) => compactNumber(String(value))}
        />
      </SideChart>
      <SideChart
        title="Фандинг"
        note={`% за период · последние ${fundingDays} дней`}
        error={funding.isError ? 'Не удалось загрузить фандинг.' : null}
        empty={funding.data?.length === 0}
      >
        <FundingChart points={funding.data ?? []} label={`Фандинг ${symbol}`} />
      </SideChart>
    </div>
  );
}

/** A metric with its label: a card of a change, or an item of the strip under the cards. */
function Metric({ label, children, card }: { label: string; children: ReactNode; card?: boolean }) {
  return (
    <div className={card ? 'rounded-xl border border-line bg-panel px-4 py-3' : ''}>
      <dt className={card ? 'text-xs text-muted' : 'text-[11px] text-muted'}>{label}</dt>
      <dd className={`font-mono ${card ? 'mt-1 text-lg font-semibold' : 'mt-1 text-sm'}`}>
        {children}
      </dd>
    </div>
  );
}

const percent = (value: string | null, digits = 2) => (
  <PercentChange value={value} digits={digits} unit=" %" />
);
const unitOf = (value: string | null, format: (value: string) => string, unit = '') =>
  value === null ? '—' : `${format(value)}${unit}`;

const defaultOwnIdea: OwnIdeaInput = { type: 'levels', interval: '15', side: 'long' };

/**
 * The ideas of the pair on the left, the chart on the right. A selected idea (`?idea=`) puts its interval, levels,
 * zones and the mark of its signal on the chart; an own idea draws its levels the same way.
 */
function IdeasHub({
  category,
  symbol,
  selectedId,
  ownInput,
  onOwnInput,
  interval,
}: {
  category: string;
  symbol: string;
  selectedId: string | undefined;
  ownInput: OwnIdeaInput;
  onOwnInput: (input: OwnIdeaInput) => void;
  /** The interval the address asks for, if any. */
  interval: string | undefined;
}) {
  const navigate = useNavigate();
  const ideas = useQuery(pairIdeasQuery(category, symbol));
  const own = useOwnIdea(category, symbol, ownInput).data;
  const listed: Idea | undefined = ideas.data?.find((item) => item.id === selectedId);
  // An idea of the address that left the list of the pair is read by its id.
  const outOfList = selectedId !== undefined && ideas.data !== undefined && listed === undefined;
  const loose = useQuery({ ...ideaQuery(selectedId ?? ''), enabled: outOfList });
  // An idea of another pair is no idea of this one.
  const looseOfPair =
    loose.data?.category === category && loose.data.symbol === symbol ? loose.data : undefined;
  const gone =
    outOfList && (loose.isError || (loose.data !== undefined && looseOfPair === undefined));
  const selected: Idea | undefined = listed ?? (outOfList ? looseOfPair : undefined);
  const ownLevels = selectedId === undefined && own?.made ? own : null;
  const shown = shownPairInterval(
    interval ?? selected?.interval ?? (ownLevels ? ownInput.interval : undefined),
  );
  const overlay = selected !== undefined || ownLevels !== null;
  const zones = useQuery({
    ...zonesQuery(category, symbol, shown),
    enabled: overlay && setupIntervals.some((item) => item.value === shown),
  });
  const levels = selected
    ? levelsOf(selected)
    : ownLevels
      ? levelsOf({ entry: ownLevels.entry!, stop: ownLevels.stop!, target: ownLevels.target! })
      : undefined;
  const select = (idea: string | undefined) =>
    void navigate({
      to: '/market/$category/$symbol',
      params: { category, symbol },
      search: idea === undefined ? (interval === undefined ? {} : { interval }) : { idea },
    });

  return (
    <div className="grid gap-4 lg:grid-cols-[22rem_1fr]">
      <div className="space-y-4">
        <section
          aria-label="Идеи пары"
          className="space-y-3 rounded-xl border border-line bg-panel px-4 py-4"
        >
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold">Идеи</h2>
            {selectedId !== undefined && (
              <button
                type="button"
                onClick={() => select(undefined)}
                className="text-xs text-accent hover:underline"
              >
                Снять выбор
              </button>
            )}
          </div>
          {ideas.isError && <p role="alert">Не удалось загрузить идеи пары.</p>}
          {ideas.isPending && <p className="text-sm text-muted">Загрузка идей…</p>}
          {outOfList && looseOfPair && (
            <p className="text-sm text-muted">Идея больше не в списке пары.</p>
          )}
          {gone && <p className="text-sm text-muted">Идеи больше нет.</p>}
          {ideas.data && (
            <PairIdeasList
              ideas={ideas.data}
              selectedId={selected?.id}
              onSelect={(idea) => select(idea.id)}
            />
          )}
        </section>
        <OwnIdeaBlock category={category} symbol={symbol} input={ownInput} onChange={onOwnInput} />
      </div>
      <PairChart
        category={category}
        symbol={symbol}
        interval={shown}
        idea={selectedId}
        levels={levels}
        zones={overlay ? zones.data?.zones : undefined}
        signalTime={
          selected && selected.interval === shown ? firedMark(selected.firedAt, shown) : undefined
        }
      />
    </div>
  );
}

export function MarketPairPage({
  category,
  symbol,
  interval,
  idea,
}: {
  category: string;
  symbol: string;
  interval?: string;
  idea?: string;
}) {
  const row = useQuery(marketPairQuery(category, symbol));
  const now = useNow();
  const [ownInput, setOwnInput] = useState(defaultOwnIdea);

  if (row.error instanceof ApiError && row.error.status === 404) {
    return (
      <section className="space-y-2">
        <h1 className="text-2xl font-bold">Пары нет</h1>
        <p className="text-sm text-muted">Такой пары нет на рынке.</p>
        <p className="text-sm">
          Вернуться к разделу{' '}
          <Link to="/market" className="font-semibold text-accent hover:underline">
            Рынок
          </Link>
          .
        </p>
      </section>
    );
  }

  if (row.isError) {
    return <p role="alert">Не удалось загрузить пару.</p>;
  }

  if (!row.data) {
    return null;
  }

  const data = row.data;
  const linear = data.category === 'linear';

  return (
    <section className="space-y-4">
      <header aria-label="Пара" className="space-y-1">
        <p className="text-xs text-muted">
          <Link to="/market" className="hover:underline">
            ← Рынок
          </Link>
        </p>
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="flex flex-wrap items-baseline gap-3">
            <h1 className="text-2xl font-bold">{data.symbol}</h1>
            <CategoryBadge category={data.category} />
            <span className="font-mono text-2xl">
              {data.price === null ? '—' : groupDigits(data.price)}
            </span>
            <span className="font-mono text-sm">
              {percent(data.change24h)} <span className="text-muted">за 24 ч</span>
            </span>
          </div>
          <UpdatedAt at={data.computedAt} now={now} />
        </div>
        <p className="text-sm text-muted">
          {linear
            ? 'Bybit · бессрочный USDT-контракт · свечи по 1 мин, OI каждые 5 мин'
            : 'Bybit · спотовая USDT-пара · свечи по 1 мин'}
        </p>
      </header>
      <section aria-label="Метрики" className="space-y-3">
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          <Metric card label="Изм. 5м">
            {percent(data.change5m)}
          </Metric>
          <Metric card label="Изм. 15м">
            {percent(data.change15m)}
          </Metric>
          <Metric card label="Изм. 1ч">
            {percent(data.change1h)}
          </Metric>
          <Metric card label="Изм. 4ч">
            {percent(data.change4h)}
          </Metric>
          <Metric card label="Изм. 24ч">
            {percent(data.change24h)}
          </Metric>
          <Metric card label="Спред">
            {unitOf(data.spread, spreadText, ' %')}
          </Metric>
        </dl>
        <dl className="grid grid-cols-2 gap-4 rounded-xl border border-line bg-panel px-5 py-4 sm:grid-cols-3 lg:grid-cols-6">
          <Metric label="Оборот 1ч, USDT">{unitOf(data.turnover1h, compactNumber)}</Metric>
          <Metric label="Оборот 24ч, USDT">{unitOf(data.turnover24h, compactNumber)}</Metric>
          {linear && (
            <>
              <Metric label="OI, USDT">{unitOf(data.openInterest, compactNumber)}</Metric>
              <Metric label="ΔOI 1ч">{percent(data.openInterestChange1h, 1)}</Metric>
              <Metric label="ΔOI 24ч">{percent(data.openInterestChange24h, 1)}</Metric>
              <Metric label="Фандинг">{unitOf(data.fundingRate, fundingText, ' %')}</Metric>
            </>
          )}
        </dl>
      </section>
      <IdeasHub
        category={category}
        symbol={symbol}
        selectedId={idea}
        ownInput={ownInput}
        onOwnInput={setOwnInput}
        interval={interval}
      />
      {linear && <SideCharts category={category} symbol={symbol} />}
      <p className="text-xs text-faint">Цифры обновляются вместе с таблицей рынка.</p>
    </section>
  );
}
