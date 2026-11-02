import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useMemo } from 'react';

import {
  botBacktestsQuery,
  type Progress,
  ProgressBar,
  progressPercent,
  rangeSummary,
  StatusBadge,
  useInvalidateOnFinish,
} from '@/modules/backtests';
import { botQuery } from '@/modules/bots';
import { instrumentsQuery, intervalLabel } from '@/modules/market-data';
import { strategiesQuery, strategyQuery } from '@/modules/strategies';
import { formatDay, formatMoment, groupDigits } from '@/shared/format/format';
import { Badge } from '@/shared/ui/Badge';

import { BotActions } from './BotActions';

interface Declared {
  readonly name: string;
  readonly default?: number | string | boolean;
}

const cell = 'px-5 py-4';

function Bar({ status, progress }: { status: string; progress: Progress }) {
  const percent = progressPercent(status, progress);

  return percent === undefined ? null : <ProgressBar percent={percent} />;
}

export function BotPage({ botId }: { botId: string }) {
  const bot = useQuery(botQuery(botId));
  const backtests = useQuery(botBacktestsQuery(botId));
  const statuses = useMemo(
    () =>
      backtests.data && Object.fromEntries(backtests.data.map((item) => [item.id, item.status])),
    [backtests.data],
  );
  useInvalidateOnFinish(statuses, botId);
  const strategyId = bot.data?.strategyId ?? '';
  const strategies = useQuery(strategiesQuery);
  const strategy = useQuery({ ...strategyQuery(strategyId), enabled: strategyId !== '' });
  const instruments = useQuery({
    ...instrumentsQuery(bot.data?.category ?? '', bot.data?.symbol ?? ''),
    enabled: bot.data !== undefined,
  });

  if (bot.isError) {
    return <p role="alert">Не удалось загрузить бота.</p>;
  }

  if (!bot.data) {
    return null;
  }

  const data = bot.data;
  const strategyName = strategies.data?.find((item) => item.id === data.strategyId)?.name ?? '';
  const quoteCoin = instruments.data?.find((item) => item.symbol === data.symbol)?.quoteCoin;
  const declared = ((strategy.data?.definition as { parameters?: Declared[] } | undefined)
    ?.parameters ?? []) as Declared[];
  const values = data.parameterValues as Record<string, unknown>;

  return (
    <section className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div className="space-y-2">
          <p className="text-xs text-muted">
            <Link to="/bots" className="hover:underline">
              Боты
            </Link>{' '}
            / {data.name}
          </p>
          <h1 className="text-2xl font-bold">{data.name}</h1>
          <p className="text-sm text-muted">
            {strategyName} · {data.category} · {data.symbol} · {intervalLabel(data.interval)} ·
            баланс {groupDigits(data.balance)}
            {quoteCoin ? ` ${quoteCoin}` : ''}
          </p>
        </div>
        <div className="flex gap-2">
          <Link
            to="/bots/$botId/edit"
            params={{ botId }}
            className="inline-flex h-8 items-center rounded-md border border-line px-3 text-sm font-semibold hover:bg-panel-2"
          >
            Изменить
          </Link>
          <BotActions botId={botId} name={data.name} />
        </div>
      </header>
      <section
        aria-label="Параметры"
        className="space-y-2 rounded-xl border border-line bg-panel px-5 py-4"
      >
        <h2 className="text-[11px] tracking-wide text-faint uppercase">Параметры</h2>
        <ul className="flex flex-wrap gap-x-10 gap-y-2 font-mono text-sm">
          {declared.map((parameter) => {
            const own = values[parameter.name];

            return (
              <li key={parameter.name} className="flex items-baseline gap-2">
                <span className="text-muted">{parameter.name}</span>
                {own !== undefined ? (
                  <span>{String(own)}</span>
                ) : (
                  <>
                    <span className="text-faint">{String(parameter.default ?? '')}</span>
                    <span className="text-xs text-faint">по умолчанию</span>
                  </>
                )}
              </li>
            );
          })}
        </ul>
      </section>
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-bold">Бэктесты</h2>
        <Link
          to="/bots/$botId/backtests/new"
          params={{ botId }}
          className="inline-flex h-8 items-center rounded-md bg-accent px-3 text-sm font-bold text-on-accent hover:brightness-110"
        >
          + Новый бэктест
        </Link>
      </div>
      {backtests.isError && <p role="alert">Не удалось загрузить бэктесты.</p>}
      <table className="w-full border-separate border-spacing-y-2 text-sm">
        <thead className="text-left text-[11px] tracking-wide text-faint uppercase">
          <tr>
            <th className={cell}>Создан</th>
            <th className={cell}>Период</th>
            <th className={cell}>Диапазоны</th>
            <th className={cell}>Комбинации</th>
            <th className={cell}>Статус</th>
            <th className={cell} />
          </tr>
        </thead>
        <tbody>
          {backtests.data?.map((backtest) => (
            <tr key={backtest.id} className="bg-panel">
              <td className={`${cell} font-mono text-muted`}>{formatMoment(backtest.createdAt)}</td>
              <td className={`${cell} font-mono`}>
                {formatDay(backtest.from)} – {formatDay(backtest.to)}
              </td>
              <td className={`${cell} font-mono`}>
                {rangeSummary(backtest.parameters as Record<string, unknown>) ?? '—'}
              </td>
              <td className={`${cell} font-mono`}>
                {groupDigits(String(backtest.progress.totalCombinations))}
              </td>
              <td className={cell}>
                <span className="flex flex-wrap items-center gap-2">
                  <StatusBadge status={backtest.status} progress={backtest.progress} />
                  <Bar status={backtest.status} progress={backtest.progress} />
                  {backtest.status === 'Failed' && backtest.error && (
                    <span className="text-err">{backtest.error}</span>
                  )}
                  {backtest.botChanged && <Badge tone="warn">бот изменился</Badge>}
                </span>
              </td>
              <td className={`${cell} text-right`}>
                <Link
                  to="/backtests/$backtestId"
                  params={{ backtestId: backtest.id }}
                  aria-label={`Открыть бэктест от ${formatMoment(backtest.createdAt)}`}
                  className="text-accent"
                >
                  →
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="text-xs text-faint">
        Список обновляется раз в 2 секунды, пока есть незавершённые бэктесты. «Бот изменился» — у
        бота новая версия стратегии, другой инструмент или интервал.
      </p>
    </section>
  );
}
