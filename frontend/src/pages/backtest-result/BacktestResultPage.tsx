import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useMemo } from 'react';

import {
  backtestQuery,
  backtestResultQuery,
  positionFills,
  resultDetailsQuery,
  whyDetailsUnavailable,
} from '@/modules/backtests';
import { botQuery } from '@/modules/bots';
import { toggleComparison, useComparison } from '@/modules/comparison';
import {
  candlesPerPoint,
  candlesText,
  markersOf,
  perPointText,
  pointsText,
  PriceEquityChart,
} from '@/modules/market-chart';
import { strategyVersionQuery } from '@/modules/strategies';
import { ApiError } from '@/shared/api/client';
import { formatMoment, showDecimal, signed } from '@/shared/format/format';
import { Button } from '@/shared/ui/Button';

import { PositionsTable } from './PositionsTable';

const card = 'space-y-1 rounded-xl border border-line bg-panel px-4 py-3';
const tones = { ok: 'text-ok', err: 'text-err' };

function Kpi({
  title,
  tone,
  children,
  note,
  exact,
}: {
  title: string;
  tone?: 'ok' | 'err';
  children: string;
  note?: string;
  exact?: string;
}) {
  return (
    <div role="group" aria-label={title} className="space-y-1">
      <div className={card}>
        <p className="text-xs text-muted">{title}</p>
        <p
          className={`font-mono text-xl font-bold whitespace-nowrap ${tone ? tones[tone] : ''}`}
          title={exact}
        >
          {children}
        </p>
      </div>
      {note && <p className="px-1 font-mono text-[11px] text-faint">{note}</p>}
    </div>
  );
}

function Fact({ title, children, exact }: { title: string; children: string; exact?: string }) {
  return (
    <div>
      <p className="text-xs text-muted">{title}</p>
      <p className="font-mono text-sm whitespace-nowrap" title={exact}>
        {children}
      </p>
    </div>
  );
}

/** Why the details are not shown: the refusal of another engine version names both versions. */
function refusal(error: Error): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'Результат не найден.';
  }

  return `Детали недоступны: ${whyDetailsUnavailable(error.message)}`;
}

export function BacktestResultPage({
  backtestId,
  combination,
}: {
  backtestId: string;
  combination: string;
}) {
  const number = Number(combination);
  const backtest = useQuery(backtestQuery(backtestId));
  const data = backtest.data;
  const bot = useQuery({ ...botQuery(data?.botId ?? ''), enabled: data !== undefined });
  const result = useQuery(backtestResultQuery(backtestId, number));
  const details = useQuery(resultDetailsQuery(backtestId, number));
  // The direction decides which side of a fill enters the position; the version is what the backtest executed.
  const version = useQuery({
    ...strategyVersionQuery(data?.strategyId ?? '', data?.strategyVersion ?? 0),
    enabled: data !== undefined,
  });
  const marked = useComparison().some(
    (item) => item.backtestId === backtestId && item.combination === number,
  );

  const item = result.data;
  const direction = (version.data?.definition as { direction?: string } | undefined)?.direction;
  // A new array on every render would put the marks on the chart again.
  const markers = useMemo(
    () =>
      details.data && (direction === 'long' || direction === 'short')
        ? markersOf(details.data.equity, positionFills(details.data.positions), direction)
        : [],
    [details.data, direction],
  );

  if (result.isError) {
    return <p role="alert">Результат не найден.</p>;
  }

  if (!data || !item) {
    return null;
  }

  const metrics = item.metrics;
  const points = details.data?.equity ?? [];
  const values = item.parameterValues as Record<string, unknown>;
  const returnTone = metrics.return.startsWith('-')
    ? 'err'
    : signed(metrics.return).startsWith('+')
      ? 'ok'
      : undefined;
  const pnlTone = metrics.pnl.startsWith('-')
    ? 'err'
    : signed(metrics.pnl).startsWith('+')
      ? 'ok'
      : undefined;

  return (
    <section className="space-y-5">
      <header className="flex items-start justify-between gap-4">
        <div className="space-y-2">
          <p className="text-xs text-muted">
            <Link to="/bots" className="hover:underline">
              Боты
            </Link>{' '}
            /{' '}
            <Link to="/bots/$botId" params={{ botId: data.botId }} className="hover:underline">
              {bot.data?.name}
            </Link>{' '}
            /{' '}
            <Link to="/backtests/$backtestId" params={{ backtestId }} className="hover:underline">
              Бэктест {formatMoment(data.createdAt)}
            </Link>{' '}
            / Комбинация #{number}
          </p>
          <h1 className="text-2xl font-bold">Комбинация #{number}</h1>
          <p className="font-mono text-sm text-muted">
            {Object.entries(values)
              .map(([name, value]) => `${name} ${String(value)}`)
              .join(' · ')}
          </p>
        </div>
        <Button
          look="ghost"
          aria-pressed={marked}
          className={marked ? 'border-accent text-accent' : ''}
          onClick={() => toggleComparison({ backtestId, combination: number })}
        >
          {marked ? '✓ В сравнении' : 'Сравнить'}
        </Button>
      </header>
      <div className="grid grid-cols-6 gap-3">
        <Kpi title="Доходность" tone={returnTone} exact={metrics.return}>
          {`${showDecimal(metrics.return, 2, true)} %`}
        </Kpi>
        <Kpi
          title="PnL"
          tone={pnlTone}
          note={`реализ. ${showDecimal(metrics.realizedPnl, 2, true)} · нереализ. ${showDecimal(metrics.unrealizedPnl, 2, true)}`}
          exact={metrics.pnl}
        >
          {showDecimal(metrics.pnl, 2, true)}
        </Kpi>
        <Kpi title="Макс. просадка" exact={metrics.maxDrawdown}>
          {`${showDecimal(metrics.maxDrawdown, 2)} %`}
        </Kpi>
        <Kpi title="Дох. / просадка" exact={metrics.returnToDrawdown ?? undefined}>
          {metrics.returnToDrawdown === null ? '—' : showDecimal(metrics.returnToDrawdown, 2)}
        </Kpi>
        <Kpi title="Сделки · прибыльных">
          {`${metrics.closedTrades} · ${metrics.winRate === null ? '—' : `${showDecimal(metrics.winRate, 2)} %`}`}
        </Kpi>
        <Kpi title="Комиссии" exact={metrics.fees}>
          {showDecimal(metrics.fees, 2)}
        </Kpi>
      </div>
      {details.isError ? (
        <div className="space-y-3">
          <p
            role="alert"
            className="rounded-md border border-[#6b5326] bg-warn-bg p-3 text-sm text-warn"
          >
            {refusal(details.error)}
          </p>
          <Link
            to="/backtests/$backtestId"
            params={{ backtestId }}
            className="text-sm text-accent hover:underline"
          >
            ← К результатам
          </Link>
        </div>
      ) : (
        <>
          <section className="space-y-3 rounded-xl border border-line bg-panel p-5">
            <div className="flex items-baseline justify-between gap-4">
              <h2 className="font-bold">
                Цена и капитал{' '}
                {points.length > 0 && (
                  <span className="ml-2 text-xs font-normal text-faint">
                    {perPointText(candlesPerPoint(points, data.interval))} ·{' '}
                    {pointsText(points.length)}
                  </span>
                )}
              </h2>
              <span className="flex gap-4 text-xs text-muted">
                <span>▬ Свечи</span>
                <span className="text-run">▬ Капитал</span>
                <span className="text-ok">▲ вход</span>
                <span className="text-err">▼ выход</span>
              </span>
            </div>
            {details.data ? (
              <PriceEquityChart points={points} markers={markers} label="Цена и капитал" />
            ) : (
              <p className="py-24 text-center text-sm text-faint">Загрузка деталей…</p>
            )}
          </section>
          <section
            aria-label="Показатели позиции"
            className="grid grid-cols-7 gap-4 rounded-xl border border-line bg-panel px-5 py-4"
          >
            <Fact title="В позиции" exact={metrics.timeInPosition}>
              {`${showDecimal(metrics.timeInPosition, 2)} %`}
            </Fact>
            <Fact title="Под водой" exact={metrics.timeUnderWater}>
              {`${showDecimal(metrics.timeUnderWater, 2)} %`}
            </Fact>
            <Fact title="Дольше всего под водой">
              {candlesText(Number(metrics.longestUnderWater))}
            </Fact>
            <Fact
              title="Макс. позиция"
              exact={`${metrics.maxPositionCost} · ${metrics.maxPositionCostPercent} %`}
            >
              {`${showDecimal(metrics.maxPositionCost, 2)} · ${showDecimal(metrics.maxPositionCostPercent, 2)} %`}
            </Fact>
            <Fact title="Макс. задействовано" exact={metrics.maxCapitalUsed}>
              {showDecimal(metrics.maxCapitalUsed, 2)}
            </Fact>
            <Fact title="Пропущено ордеров">{String(metrics.skippedOrders)}</Fact>
            <Fact title="Исполнений">{String(metrics.fills)}</Fact>
          </section>
          {details.data && <PositionsTable positions={details.data.positions} />}
        </>
      )}
      <p className="text-xs text-faint">
        Детали пересчитываются по версии стратегии и правилам бэктеста. Метка стоит на точке, в
        которую попало время исполнения; точные значения — в таблице позиций.
      </p>
    </section>
  );
}
