import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useMemo, useState } from 'react';

import {
  backtestQuery,
  cancelBacktest,
  isFinished,
  StatusBadge,
  useInvalidateOnFinish,
} from '@/modules/backtests';
import { botQuery } from '@/modules/bots';
import { intervalLabel } from '@/modules/market-data';
import { strategiesQuery } from '@/modules/strategies';
import {
  combinationsWord,
  formatFullMoment,
  formatMoment,
  groupDigits,
} from '@/shared/format/format';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

import { ProgressPanel } from './ProgressPanel';
import { ResultsTable } from './ResultsTable';

const day = (iso: string) => formatFullMoment(iso).slice(0, 10);

export function BacktestPage({ backtestId }: { backtestId: string }) {
  const queryClient = useQueryClient();
  const backtest = useQuery(backtestQuery(backtestId));
  const data = backtest.data;
  const bot = useQuery({ ...botQuery(data?.botId ?? ''), enabled: data !== undefined });
  const strategies = useQuery(strategiesQuery);
  const [confirming, setConfirming] = useState(false);
  const statuses = useMemo(() => data && { [data.id]: data.status }, [data]);
  useInvalidateOnFinish(statuses, data?.botId);
  const cancel = useMutation({
    mutationFn: () => cancelBacktest(backtestId),
    onSettled: () => {
      setConfirming(false);

      return queryClient.invalidateQueries({ queryKey: ['backtests', backtestId] });
    },
  });

  if (backtest.isError) {
    return <p role="alert">Не удалось загрузить бэктест.</p>;
  }

  if (!data) {
    return null;
  }

  const finished = isFinished(data.status);
  const strategyName = strategies.data?.find((item) => item.id === data.strategyId)?.name ?? '';
  const combinations = data.progress.totalCombinations;
  const lateStart =
    data.actualStart !== null && Date.parse(data.actualStart) > Date.parse(data.from);

  return (
    <section className="space-y-6">
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
            / Бэктест {formatMoment(data.createdAt)}
          </p>
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-bold">Бэктест</h1>
            {finished && <StatusBadge status={data.status} progress={data.progress} />}
          </div>
          <p className="text-sm text-muted">
            {strategyName} v{String(data.strategyVersion)} · {data.category} · {data.symbol} ·{' '}
            {intervalLabel(data.interval)} · {day(data.from)} – {day(data.to)} ·{' '}
            {groupDigits(String(combinations))} {combinationsWord(combinations)} · движок v
            {String(data.engineVersion)}
          </p>
        </div>
        {!finished && (
          <Button
            look="danger"
            className="border border-[#5a2a2e]"
            onClick={() => setConfirming(true)}
          >
            Отменить
          </Button>
        )}
      </header>
      {cancel.isError && <p role="alert">{cancel.error.message}</p>}
      {data.status === 'Failed' && data.error && (
        <p
          role="alert"
          className="rounded-md border border-[#5a2a2e] bg-err-bg p-3 text-sm text-err"
        >
          Расчёт не удался: {data.error}
        </p>
      )}
      {data.botChanged && (
        <p
          role="status"
          className="rounded-md border border-[#6b5326] bg-warn-bg p-3 text-sm text-warn"
        >
          Бот изменился после этого бэктеста. Результаты посчитаны по версии v
          {String(data.strategyVersion)} стратегии {strategyName}.
        </p>
      )}
      {!finished && (
        <ProgressPanel status={data.status} progress={data.progress} speed={data.speed} />
      )}
      {lateStart && (
        <p className="text-sm text-muted">
          Первая свеча — {formatFullMoment(data.actualStart!)}, позже начала периода (
          {formatFullMoment(data.from)}).
        </p>
      )}
      {data.gaps.length > 0 && (
        <p className="text-sm text-muted">
          Пропуски в свечах:{' '}
          {data.gaps
            .map((gap) => `${formatFullMoment(gap.from)} – ${formatFullMoment(gap.to)}`)
            .join('; ')}
        </p>
      )}
      <ResultsTable
        backtestId={backtestId}
        parameters={data.parameters as Record<string, unknown>}
        finished={finished}
        combinations={combinations}
      />
      {confirming && (
        <Dialog
          title="Отменить бэктест?"
          actions={
            <>
              <Button onClick={() => setConfirming(false)}>Не отменять</Button>
              <Button look="primary" onClick={() => cancel.mutate()}>
                Отменить бэктест
              </Button>
            </>
          }
        >
          <p className="text-sm text-muted">Посчитанные результаты сохранятся.</p>
        </Dialog>
      )}
    </section>
  );
}
