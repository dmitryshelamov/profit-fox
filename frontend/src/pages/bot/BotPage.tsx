import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect, useMemo } from 'react';

import {
  botBacktestsQuery,
  type Progress,
  ProgressBar,
  progressPercent,
  rangeSummary,
  StatusBadge,
  useInvalidateOnFinish,
} from '@/modules/backtests';
import { botPositionsQuery, botQuery, isFollowed } from '@/modules/bots';
import { instrumentsQuery, intervalLabel } from '@/modules/market-data';
import { strategiesQuery, strategyQuery } from '@/modules/strategies';
import { formatDay, formatMoment, groupDigits } from '@/shared/format/format';
import { Badge } from '@/shared/ui/Badge';

import { BotActions } from './BotActions';
import { LiveControls } from './LiveControls';
import { LivePositions } from './LivePositions';

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
  const bot = useQuery({
    ...botQuery(botId),
    refetchInterval: (query) => (isFollowed(query.state.data) ? 5000 : false),
  });
  const positions = useQuery({
    ...botPositionsQuery(botId),
    refetchInterval: isFollowed(bot.data) ? 5000 : false,
  });
  const queryClient = useQueryClient();
  const status = bot.data?.status;
  const closing = bot.data?.closeRequested;

  // A Worker may finish between two polls: the positions are read again whenever the bot moves on, its last state too.
  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: botPositionsQuery(botId).queryKey });
  }, [queryClient, botId, status, closing]);
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
  const listed = instruments.data?.find((item) => item.symbol === data.symbol);
  const quoteCoin = listed?.quoteCoin;
  const followed = isFollowed(data);
  const current = positions.data?.find((position) => position.status === 'open');
  // The side of the exit comes with the position: the bot trades the version fixed at its start, not the current one.
  const close = current
    ? `${current.exitSide === 'buy' ? 'Покупка' : 'Продажа'} по рынку ${current.quantity}${listed?.baseCoin ? ` ${listed.baseCoin}` : ''}.`
    : undefined;
  const start =
    data.exchangeAccount &&
    `Бот начнёт торговать на аккаунте «${data.exchangeAccount.name}»: ${data.category} · ${data.symbol}, баланс ${groupDigits(data.balance)}${quoteCoin ? ` ${quoteCoin}` : ''}. Ордера уходят на биржу с реальными деньгами.`;
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
          <p className="flex items-center gap-2 text-sm text-muted">
            Аккаунт:{' '}
            {data.exchangeAccount ? (
              <Link
                to="/accounts/$accountId"
                params={{ accountId: data.exchangeAccount.id }}
                className="font-semibold text-accent hover:underline"
              >
                {data.exchangeAccount.name}
              </Link>
            ) : (
              'Без аккаунта'
            )}
          </p>
        </div>
        {followed ? (
          <p className="max-w-56 text-right text-xs text-faint">
            {data.closeRequested
              ? 'Изменить или удалить бота можно, когда позиция закроется.'
              : 'Остановите бота, чтобы изменить или удалить его.'}
          </p>
        ) : (
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
        )}
      </header>
      <LiveControls
        botId={botId}
        status={data.status}
        reason={data.statusReason}
        closing={data.closeRequested}
        start={start ?? undefined}
        close={close}
        ready={!instruments.isPending}
      />
      {positions.isError && <p role="alert">Не удалось загрузить позиции.</p>}
      {positions.data && positions.data.length > 0 && (
        <LivePositions
          positions={positions.data}
          coins={{ base: listed?.baseCoin ?? '', quote: quoteCoin ?? '' }}
        />
      )}
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
